using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;
using Ygo.Engine.Replays;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Spectator mode: two bot characters duel while the board renders each visible event.
    /// The matchup picker chooses each seat's character and deck; Random picks two different
    /// characters with decks from their own pools; Rematch replays the matchup with a new shuffle.
    /// Owns the engine objects and paces the duel loop against real time.
    /// </summary>
    public sealed partial class DuelController : MonoBehaviour
    {
        private static readonly float[] Speeds = { 0.5f, 1, 2, 4, 8, 16, 60 };
        private const int MaxStepsPerTick = 2000;

        [SerializeField] private int speedIndex = 3;
        [SerializeField] private int seed = 1;

        private static CardPack? _cardPack;
        private IScriptProvider _scripts = null!;
        private BoardView _view = null!;
        private Duel? _duel;
        private DuelRunner? _runner;
        private BoardState _board = new BoardState();
        private bool _sawVisibleEvent;
        // What happened since the last redraw, for the board's animations.
        private readonly List<DuelEvent> _events = new List<DuelEvent>();
        // Replays: the duel being recorded (live play), or the one being watched.
        private Replay? _recording;
        private Replay? _watching;
        private ReplayLibrary _replays = null!;
        private ReplayBrowser _browser = null!;
        // Campaign: your save, the ladder screen, the shop, and the opponent of the campaign duel being played (if any).
        private CampaignState _campaign = null!;
        private GradingDeskScreen _gradingDesk = null!;
        private CampaignScreen _campaignScreen = null!;
        private ShopScreen _shop = null!;
        // The circuit match being played, if this is a campaign duel.
        private (int circuit, int match)? _campaignMatch;
        private bool _editorFromCampaign, _editorFromMenu, _browserFromMenu;
        /// <summary>The Recipe Book was opened from the shop's header; the editor was opened from that book (both go back to the shop).</summary>
        private bool _bookFromShop, _editorFromShop;
        private TitleScreen _title = null!;
        private MainMenu _menu = null!;
        private SettingsScreen _settings = null!;
        private AboutScreen _about = null!;
        private ImportScreen _import = null!;
        private int _ticks;          // redraws so far in this duel (a replay position)
        private bool _sawNewTurn;
        private bool _shownReplayEnd;
        private bool _paused;
        private bool _stepRequested;
        private float _timer;
        private DeckLibrary _library = null!;
        private DeckEditor _editor = null!;
#if ENABLE_INPUT_SYSTEM
        private ControllerSupport _controller = null!;
#endif
        private List<Personality> _roster = new List<Personality>();
        private Personality[] _players = Array.Empty<Personality>();
        /// <summary>
        /// The opponent's cards that became known this duel, for the deck editor's Seen in duels (written to seen.txt when
        /// the duel ends). Null when the duel isn't yours to play: replays, show duels, watched duels, lessons, check runs.
        /// </summary>
        private SeenCards? _seen;
        private bool _announcedResult;
        private List<string> _allDecks = new List<string>();
        // Picker selection per seat: index into _roster (-1 = you, bottom seat only), and deck name.
        private readonly int[] _pickCharacter = new int[2];
        private HumanDuelist? _human;
        private bool _answerReady;
        private readonly string[] _pickDeck = { "", "" };

        private void Start()
        {
            Application.targetFrameRate = 60;
            Application.runInBackground = true; // keep the duel going while another window has focus
            _cardPack ??= CardPack.Load(Path.Combine(Application.streamingAssetsPath, "cards.bin"));
            _scripts = new FileScriptProvider(FindDataFolder("CardScripts", Path.Combine("vendor", "CardScripts")));

            // Player files live in mods/: card pictures (the game ships with none), and decks made in the editor.
            var cli = Environment.GetCommandLineArgs();
            string? CliArg(string name)
            {
                var i = Array.IndexOf(cli, name);
                return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null;
            }
            // The built game keeps its data in ~/Library/Application Support/YgoCampaign/YgoCampaign/mods. That folder
            // is fixed here rather than following the app's name (Unity's persistentDataPath does), so renaming the app
            // never strands a save, decks, or card pictures.
            var modsRoot = CliArg("-mods") ?? (Application.isEditor
                ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "mods"))
                : Application.platform == RuntimePlatform.OSXPlayer
                    ? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library", "Application Support", "YgoCampaign", "YgoCampaign", "mods")
                    : Path.Combine(Application.persistentDataPath, "mods"));
            // `-hdpics <path>` (repeat it for more folders; `-hdpics off` for none): HD picture folders for this run instead of
            // the settings' hd_pics lines.
            var hdPics = new List<string>();
            for (int i = 0; i + 1 < cli.Length; i++)
                if (cli[i] == "-hdpics") hdPics.Add(cli[i + 1]);
            CardView.Art = new CardArt(_cardPack, modsRoot, CliArg("-pics"), hdPics.Count > 0 ? hdPics : null);
            Portraits.Init(modsRoot);
            Playmat.InitFolders(modsRoot);
            _modsRoot = modsRoot;
            // Real-world printings and rarities (optional: without the file every card is drawn plain).
            var printings = Path.Combine(Application.streamingAssetsPath, "printings.bin");
            if (CardFoil.Printings == null && File.Exists(printings)) CardFoil.Printings = PrintingIndex.Load(printings);
            _library = new DeckLibrary(FindDataFolder("decks", "decks"), Path.Combine(modsRoot, "decks"));

            // Everyone with a profile speaks in scenes (Baz, Moth and Odile never duel); the roster is who duels.
            var cast = Personality.LoadFolder(FindDataFolder("personalities", "personalities")).ToList();
            _roster = cast.Where(p => p.Decks.Any(_library.Contains)).ToList();
            if (_roster.Count < 2) _roster = new List<Personality> { Personality.Balanced, Personality.Balanced };

            CardWear.Prepare(); // the wear atlas, made off the main thread
            _view = gameObject.AddComponent<BoardView>();
            _view.Build(_cardPack);
            _view.YourPoster = () => _campaign?.Profile is Profile p ? Posters.Current(_campaign.Circuits, p) : null;
            // The developer panel (the old side panel: matchup picker, decision list, log, test buttons): developer mode
            // only (-dev, the editor, a development build, the checks: DuelController.DevMode). F12 or -devpanel shows it;
            // the checks written against it turn it on, the campaign and menu checks use the play layout.
            var launch = Environment.GetCommandLineArgs();
            DeveloperMode = DeveloperLaunch(launch);
            _view.DeveloperMode = DeveloperMode;
            _view.DuelToolsLock = DuelToolsLock;
            _view.DevPanel = DeveloperMode && new[] { "-devpanel", "-boardcheck", "-replaycheck", "-tagcheck", "-fxshots", "-matcheck", "-autoshot", "-autoshot-end", "-autoclick" }
                .Any(a => Array.IndexOf(launch, a) >= 0);
            _view.DevPanelClicked += ToggleDevPanel;
            _view.SettingsClicked += () => { _settingsFromDuel = true; _pausedBeforeSettings = _paused; _paused = true; UpdateControls(); _settings.Open(); };
            _view.IsPaused = () => _paused;
            // Screens over the board (a story, the menu, Settings): the board's keys (Space, P, B, E) are theirs then.
            _view.KeysElsewhere = () => _storyScreen.IsOpen || _menu.IsOpen || _settings.IsOpen || _about.IsOpen || _import.IsOpen || _editor.IsOpen || _shop.IsOpen;
            _view.CanConcede = () => _runner != null && !_runner.IsFinished && _human != null && _watching == null && _lessonJudge == null;
            _view.Concede = ConcedeDuel;
            _view.CanSkipTurn = CanSkipTurn;
            _view.SkipTurnClicked += StartSkip;
            _view.PauseClicked += () => { _paused = !_paused; UpdateControls(); };
            _view.StepClicked += () => { _paused = true; _stepRequested = true; UpdateControls(); };
            _view.SpeedChanged += delta => { speedIndex = Mathf.Clamp(speedIndex + delta, 0, Speeds.Length - 1); UpdateControls(); };
            _view.RematchClicked += () =>
            {
                if (DuelToolRefused("Rematch")) return; // a campaign duel isn't played again from the developer panel
                // The finished duel's results, still waiting their 2 s, stay away from what comes next (its scenes stay queued).
                _duelSerial++;
                // A bracket result stands: its duel isn't played again (a draw is, from the bracket).
                if (_bracketMatch != null || _bracketWatch) { ShowBracket(); return; }
                // An alley result stands too: the table's next seat (or the same one) from its versus.
                if (_alleyTable is int alleyTable) { ShowAlleyVersus(alleyTable, _alleyOpp?.Seat == 0 ? _alleyOpp.Character : null); return; }
                // A ladder duel again from its versus: the sitting may have moved on, and a rung taken can't be played again before the top.
                if (_ladderDuel is LadderSeat ladderSeat) { ShowLadderVersus(ladderSeat.Key); return; }
                seed++;
                Action again;
                if (_campaignMatch is { } m) again = () => StartCampaignDuel(m.circuit, m.match);
                else if (_campaignTag is { } t) again = () => StartCampaignTag(t.cup, t.match, t.partner);
                else if (_campaignCup is { } lc) again = () => StartCupDuel(lc.cup, lc.match);
                else if (_campaignRound is int lr) again = () => StartRoundDuel(lr);
                else if (_guestDuel is { } g) again = () => StartGuestDuel(g.guest, g.tag, g.partner, _loanerCircuit);
                else { var tag = _tag; again = () => NewDuel(null, tag); }
                // A new coin toss for the rematch too.
                var deckNames = _recording?.DeckNames ?? Array.Empty<string>();
                var measured = _campaignTag is { } ct ? MeasuredToss(Circuits.TagKey(ct.cup, ct.match), ct.partner, _campaign.PartnerDeck(_roster.First(p => p.Id == ct.partner)))
                    : _campaignMatch is { } cm ? MeasuredToss(Profile.MatchKey(cm.circuit, cm.match))
                    : _campaignCup is { } lm ? MeasuredToss(_campaign.LibraryAdvice.For(Profile.MatchKey(lm.cup, lm.match)))
                    : _campaignRound is int rr ? MeasuredToss(RoundMeasured(rr)) : null;
                CoinToss(TeamName(0), TeamName(1), deckNames.ElementAtOrDefault(0) ?? "", deckNames.ElementAtOrDefault(1) ?? "", again, youChoose: _human != null,
                    tag: _tag != null, measured: measured);
            };
            _view.CharacterCycled += CycleCharacter;
            _view.DeckCycled += (seat, delta) =>
            {
                CycleDeck(seat, delta);
            };
            // The board's Start and Random (free play): a coin toss first, as everywhere.
            string SideName(int seat) => _pickCharacter[seat] < 0 ? "You" : _roster[_pickCharacter[seat]].Name;
            void TossThenDuel() => CoinToss(SideName(0), SideName(1), _pickDeck[0], _pickDeck[1], () => NewDuel(), youChoose: _pickCharacter[0] < 0);
            // (A duel on its way: a results screen still waiting its 2 s stays away from the toss.)
            _view.StartClicked += () => { if (DuelToolRefused("Start Duel")) return; _duelSerial++; seed++; TossThenDuel(); };
            _view.RandomClicked += () => { if (DuelToolRefused("Random")) return; _duelSerial++; seed++; PickRandom(); TossThenDuel(); };
            _view.Prompt.Answered += response =>
            {
                // Checks: how long each decision waited on screen (a slow one is logged with what it was).
                if (PromptTiming && _promptShownAt > 0 && Time.realtimeSinceStartup - _promptShownAt > 1.5f)
                    Debug.Log($"prompt timing: \"{_promptShownTitle}\" waited {Time.realtimeSinceStartup - _promptShownAt:0.0} s (turn {_board.Turn}, {Phase.Name(_board.Phase)})");
                _human?.Submit(response);
                _answerReady = true;
            };

            _allDecks = _library.Names.ToList();

            // The deck editor (its own screen); the duel pauses while it's open.
            // The built game has every list in StreamingAssets/lflists; in the editor they come from the repo.
            var lists = FindDataFolder("lflists", Path.Combine("vendor", "LFLists"));
            var historic = Path.GetFullPath(Path.Combine(lists, "..", "..", "lflists"));
            var banlists = Banlist.LoadFolders(lists.StartsWith(Application.streamingAssetsPath)
                ? new[] { lists, Path.Combine(modsRoot, "lflists") }
                : new[] { lists, historic, Path.Combine(modsRoot, "lflists") });
            _editor = gameObject.AddComponent<DeckEditor>();
            _editor.Init(_cardPack, _scripts, _library, banlists, _roster);
            _editor.SetAnalysisData(Archetypes.Load(Path.Combine(FindDataFolder("CardScripts", Path.Combine("vendor", "CardScripts")), "archetype_setcode_constants.lua")));
            _editor.WishlistChanged += (deck, wants) =>
            {
                if (_campaign?.Profile is not Profile profile || _editor.OwnedCards == null) return;
                profile.Wishlist.Clear();
                foreach (var w in wants) profile.Wishlist[w.Key] = w.Value;
                profile.WishlistFor = deck;
                _campaign.Save();
            };
            // A Recipe set: Gus's owed bounties pay from it now (the editor shows what they paid).
            _editor.PayOwed = () => _campaign?.Profile != null && _editor.OwnedCards != null ? _campaign.PayOwed() : "";
            // Save as new in the campaign: the new deck is the one you play (the old one stays in your decks).
            _editor.SavedAsNew += deck =>
            {
                if (_campaign?.Profile is not Profile profile || _editor.OwnedCards == null || _campaign.IsLoanerDeck(deck)) return;
                profile.Deck = deck;
                _campaign.Save();
            };
            _editor.Closed += () =>
            {
                if (_editorFromCampaign)
                {
                    _editorFromCampaign = false;
                    // Built from the Recipe Book over the shop: back to the shop (not a visit); else the campaign.
                    if (_editorFromShop) { _editorFromShop = false; _shop.Open(visit: false); }
                    else _campaignScreen.Open();
                }
                else if (_editorFromMenu)
                {
                    _editorFromMenu = false;
                    _menu.Open();
                }
                _allDecks = _library.Names.ToList();
                for (int seat = 0; seat < 2; seat++)
                    if (!_allDecks.Contains(_pickDeck[seat])) _pickDeck[seat] = DeckChoicesFor(seat)[0];
                ShowPicks();
            };
            _view.DecksClicked += () => { if (!DuelToolRefused("The Deck Editor")) _editor.Open(_pickDeck[0]); };

            // Campaign: circuits, DP, the card shop, and your collection (mods/save/profile.txt).
            Sounds.Create(gameObject);
            _campaign = new CampaignState(modsRoot, Path.Combine(FindDataFolder("campaign", "campaign"), "circuits.txt"),
                _cardPack, CardFoil.Printings, _library, _roster, banlists);
            _campaign.Cast = cast.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
            // The editor tells texts apart by entry and says where each original plays (campaign/texts.txt).
            _editor.SetTexts(_campaign.Texts);
            CardDetail.Texts = _campaign.Texts;
            _editor.VersusLine = (deck, texts, owned) => _campaign.TextsLine(deck, texts, owned);
            CardFoil.OwnedRarity = _campaign.Owned;
            CardDetail.CopiesOf = code => _campaign.Profile?.Collection.CopiesOf(code) ?? Array.Empty<CardCopy>();
            CardFoil.OwnedLook = code => _campaign.Profile?.Collection.CopiesOf(code) is { Count: > 0 } copies ? copies[0].Look : (CopyLook?)null;
            _campaignScreen = gameObject.AddComponent<CampaignScreen>();
            _campaignScreen.Init(_campaign, _library, _cardPack);
            _shop = gameObject.AddComponent<ShopScreen>();
            _shop.Init(_campaign, _cardPack);
            _shop.Closed += () => _campaignScreen.Open();
            _trader = gameObject.AddComponent<TraderScreen>();
            _trader.Init(_campaign, _cardPack);
            _shop.TraderClicked += () => _trader.Open();
            _gradingDesk = gameObject.AddComponent<GradingDeskScreen>();
            _gradingDesk.Init(_campaign, _cardPack);
            _shop.GradingClicked += () => _gradingDesk.Open();
            _gradingDesk.Closed += () => { if (_shop.IsOpen) _shop.Open(visit: false); };
            _structures = gameObject.AddComponent<StructureScreen>();
            _structures.Init(_campaign, _library, _cardPack);
            _shop.StructureClicked += () => _structures.Open();
            _structures.Closed += () => _shop.RefreshHeader();
            // The Library Trader opens from a cup's panel and goes back to it; the shop's goes back to the shop.
            _trader.Closed += () => { _shop.RefreshHeader(); if (!_shop.IsOpen) _campaignScreen.Open(); };
            _campaignScreen.ShopClicked += () => { MainStreetTheme(); _campaignScreen.Close(); _shop.Open(); };
            // Scout from the campaign screen: the report on a match's decks, then back to the campaign.
            _campaignScreen.ScoutClicked += them => { _campaignScreen.Close(); _scenes.ShowScoutReport(Scouts(them.ToArray()), () => _campaignScreen.Open()); };
            _campaignScreen.MenuClicked += () => { MainStreetTheme(); _menu.Open(); };
            _campaignScreen.EditDeck += deck =>
            {
                _editorFromCampaign = true;
                _campaignScreen.Close();
                OpenCampaignEditor(deck);
            };
            // Your edit of a partner's deck, under the cup's list (it answers to it like your decks).
            _campaignScreen.EditPartnerDeck += (deck, circuit) =>
            {
                _editorFromCampaign = true;
                _campaignScreen.Close();
                _editor.Open(deck, EditorCollection(deck), _campaign.ListFor(circuit)?.Name, _campaign.CircuitTexts(circuit));
            };
            _recipeBook = gameObject.AddComponent<RecipeBookScreen>();
            _recipeBook.Init(_campaign, _library, _cardPack);
            _campaignScreen.RecipesClicked += () => { MainStreetTheme(); _bookFromShop = false; _campaignScreen.Close(); _recipeBook.Open(); };
            // The Recipe Book opens from the campaign screen and from the shop's header, and goes back to where it was opened
            // (the shop again after a lesson, which hid it: see TakeLesson).
            _shop.RecipeClicked += () => { _bookFromShop = true; _recipeBook.Open(); };
            _recipeBook.Closed += () =>
            {
                if (_bookFromShop && !_shop.IsOpen) { _bookFromShop = false; _shop.Open(visit: false); }
                else if (_shop.IsOpen) { _bookFromShop = false; _shop.RefreshHeader(); }
                else _campaignScreen.Open();
            };
            _recipeBook.WatchLesson += PrepareLesson;
            // Build and "Customize as loaner" open the editor; from the shop's book the shop steps aside (its canvas is
            // over the editor's) and comes back when the editor closes.
            _recipeBook.EditDeck += deck =>
            {
                _editorFromCampaign = true;
                if (_bookFromShop)
                {
                    _bookFromShop = false;
                    _editorFromShop = true;
                    _shop.Hide();
                }
                OpenCampaignEditor(deck);
            };
            _editor.WhereToGet = code => _campaign.WhereToGet(code);
            _editor.PassAllowance = () => _campaign.PassAllowance;
            _editor.NoPass = _campaign.NoPass;
            _editor.EraLimited = () => _campaign.Profile?.EraLimited ?? false;
            // In the campaign the editor's Recipe button only uses recipes you've unlocked.
            _editor.RecipeDecks = () => _campaign.Profile == null ? new List<string>()
                : RecipeBook.Unlocked(_campaign.Circuits, _campaign.Profile).Select(r => r.Deck).Distinct().ToList();
            // Your own recipes (imported .ydk files, docs/dev/YDK-IMPORT-2026-11-07.md): the Recipe button's first ones, and
            // the campaign editor's Import .ydk keeps a file as one.
            _editor.MyRecipeDecks = () => _campaign.Profile == null ? new List<Deck>() : _campaign.MyRecipes.All();
            _editor.ImportRecipe = path => (_campaign.ImportRecipe(path, out var status), status);
            _scenes = gameObject.AddComponent<CampaignScenes>();
            _scenes.Init(_cardPack);
            _campaignScreen.Duel += (circuit, match) => ShowVersus(circuit, match);
            _campaignScreen.CupDuel += (cup, match) => ShowCupVersus(cup, match);
            _campaignScreen.RoundDuel += round => ShowRoundVersus(round);
            _campaignScreen.LadderDuel += key => ShowLadderVersus(key); // the Champion's Ladder (DuelController.Ladder)
            _campaignScreen.CupTrader += cup => { _campaignScreen.Close(); _trader.Open(_campaign.CupRules(cup), _campaign.Library.Cups[cup].Name); };
            _campaignScreen.LessonsClicked += () => { MainStreetTheme(); _campaignScreen.Close(); ShowLessons(); };
            _campaignScreen.GuestsClicked += () => { MainStreetTheme(); _campaignScreen.Close(); ShowGuests(); };
            // Library Locals: a cleared cup's bracket (its button, entry dialog and screen).
            InitBrackets();
            // The Back Alley: its tables' Duel and Encore (DuelController.Alley).
            InitAlley();
            // Story moments: after choosing a partner, starting the campaign, New Game+, and after results.
            _story = Story.Load(Path.Combine(FindDataFolder("campaign", "campaign"), "story.txt"));
            Playmat.LoadSignatures(Path.Combine(FindDataFolder("campaign", "campaign"), "mats.txt"));
            _storyScreen = gameObject.AddComponent<StoryScreen>();
            _storyScreen.Init(_cardPack, _library, _roster.Concat(cast.Where(p => _roster.All(r => r.Id != p.Id))));
            _storyScreen.PartnerDeckName = p => _campaign.SpokenTitle(_campaign.PartnerDeck(p));
            _storyScreen.PlaceOf = scene => StoryPlaces.Of(scene, _campaign.AlleyData, _campaign.Library);
            _campaignScreen.TagDuel += (cup, match, partner) =>
                PlayStory(new[] { "partner", "partner:" + partner }, partner, () => ShowTagVersus(cup, match, partner));
            _campaignScreen.CampaignStarted += () => { _campaignScreen.Close(); PlayStory(new[] { "start" }, "", OfferFirstDuel); };
            _campaignScreen.NewGamePlusStarted += () => { _campaignScreen.Close(); PlayStory(new[] { "ngplus" }, "", () => _campaignScreen.Open()); };
            // The campaign screen in the board's place: a results screen still waiting its 2 s stays away (DuelController.Results).
            _campaignScreen.Opened += () => _duelSerial++;
            // The Library shown and its cups opened: their scenes the first time (nothing to close the screen for after).
            _campaignScreen.StoryEvents += events =>
            {
                if (_campaign.Profile is not Profile storyProfile || _story.Pending(storyProfile, events).Count == 0) return;
                _campaignScreen.Close();
                // Queued scenes (a notice's, a result whose screen was skipped) come off the queue as the screen plays them,
                // a batch at a time with its own grants, placeholders and partner; then the screen's own events.
                var batches = _campaign.TakeQueuedStory(events);
                var queued = new HashSet<string>(batches.SelectMany(q => q.Events));
                batches.Add(new CampaignState.QueuedStory { Events = events.Where(e => !queued.Contains(e)).ToList() });
                PlayResultStory(batches, () => _campaignScreen.Open());
            };

            // Replays: every finished duel is kept (the last 30), and any duel can be saved or shared as a code.
            _replays = new ReplayLibrary(modsRoot);
            _browser = gameObject.AddComponent<ReplayBrowser>();
            _browser.Init(_replays);
            _browser.Watch += replay =>
            {
                _browserFromMenu = false;
                WatchReplay(replay, replay.OpenAt);
                if (replay.OpenAt > 0) _view.AppendLog($"The code opens at turn {_board.Turn}. Press Play to watch from here, or Restart for the beginning.");
            };
            _view.ReplaysClicked += () => { if (DuelToolRefused("Replays")) return; _paused = true; UpdateControls(); _browser.Open(); };
            _view.SaveReplayClicked += SaveReplay;
            _view.CopyCodeClicked += () =>
            {
                if (_watching == null) return;
                // From the middle of a replay, the code opens right here.
                var code = _watching.ToCode(_ticks);
                GUIUtility.systemCopyBuffer = code;
                _view.AppendLog(_ticks > 0
                    ? $"Copied the replay code to the clipboard ({code.Length} characters). It opens here, at turn {_board.Turn}."
                    : $"Copied the replay code to the clipboard ({code.Length} characters).");
            };
            _view.RestartClicked += () => { if (_watching != null) WatchReplay(_watching, 0); };
            _view.BackClicked += () => { if (_watching != null) WatchReplay(_watching, Math.Max(0, _ticks - 1)); };
            _view.NextTurnClicked += SkipToNextTurn;
            _view.MomentsClicked += () =>
            {
                if (_watching == null) return;
                if (_moments == null) { _view.AppendLog("Finding the key moments..."); return; }
                _paused = true;
                UpdateControls();
                _view.ShowMoments(_moments, _ticks, _lessonTurn);
            };
            _view.MomentPicked += JumpReplay;
            _browser.Closed += () => { if (_browserFromMenu) { _browserFromMenu = false; _menu.Open(); } };

            // Title screen, main menu, settings. A bot duel runs behind the title in attract mode.
            _title = gameObject.AddComponent<TitleScreen>();
            _title.Init(_cardPack);
            _menu = gameObject.AddComponent<MainMenu>();
            _menu.Init();
            _settings = gameObject.AddComponent<SettingsScreen>();
            _settings.Init();
            _settings.Speeds = Speeds;
            _settings.GetSpeed = () => speedIndex;
            _settings.SetSpeed = i =>
            {
                speedIndex = i;
                PlayerPrefs.SetInt("speed", i);
                UpdateControls();
            };
            speedIndex = Mathf.Clamp(PlayerPrefs.GetInt("speed", speedIndex), 0, Speeds.Length - 1);
            _settings.Closed += () =>
            {
                // Opened from the duel's gear: back to the duel as it was (running, or paused if it was), not the menu.
                if (_settingsFromDuel)
                {
                    _settingsFromDuel = false;
                    _paused = _pausedBeforeSettings;
                    UpdateControls();
                    if (_board != null) _view.Render(_board); // presentation settings (the playable tabs) show at once
                    return;
                }
                _menu.Open();
            };
            // About (licenses, credits, source code), from the menu and back to it.
            _about = gameObject.AddComponent<AboutScreen>();
            _about.Init();
            _about.Closed += () => _menu.Open();
            // Import Cards (card pictures and the card back, into mods/cards), from the menu and back to it.
            _import = gameObject.AddComponent<ImportScreen>();
            _import.Init();
            _import.Closed += () => _menu.Open();
            _title.Continue += () => { _title.Close(); _paused = true; UpdateControls(); _menu.Open(); };
            _title.Attract += on =>
            {
                _view.AttractMode = on; // just the duel behind the logo: no controls
                if (!on) return;
                // A fresh bot duel to watch behind the logo.
                seed++;
                PickRandomBots();
                NewDuel();
                _paused = false;
                speedIndex = 3;
                UpdateControls();
            };
            // Leaving a held alley duel, or an alley bracket duel, for the menu is a loss (DuelController.Alley, .Brackets);
            // any other campaign duel waits behind the menu, which keeps its other doors shut until it's over (.DevMode).
            _view.MenuClicked += OpenMenuFromDuel;
            _menu.MidDuel = () => CampaignDuelLive ? "Your duel is waiting." : null;
            _menu.CanConcede = () => _view.CanConcede?.Invoke() == true;
            _menu.QuitLoses = () => _campaign.Profile?.LiveDuel != null;
            _view.ResponsesClicked += CycleResponses;
            _view.HoldingAsk = HoldingAsk;
            _menu.FindInfo = code => _cardPack.Get(code);
            _menu.CampaignLine = () =>
            {
                var profile = _campaign.Profile;
                if (profile == null) return "Start your campaign: pick a starter deck, open packs, and climb from the local shop to the Gauntlet.";
                var circuits = _campaign.Circuits;
                var current = circuits.Current(profile);
                var c = circuits.All[current];
                var won = Enumerable.Range(0, c.Matches.Count).Count(m => profile.HasWon(current, m));
                return circuits.ClearedCount(profile) == circuits.All.Count
                    ? $"<color=#FFC940>Champion!</color> All {circuits.All.Count} circuits cleared · {profile.Dp:N0} DP"
                    : $"Continue: <b>{c.Name}</b> ({won}/{c.Matches.Count} won) · <color=#FFC940>{profile.Dp:N0} DP</color> · {profile.Collection.Total} cards";
            };
            _menu.FreeDuelLine = () => $"Any card, any deck: play or watch {FreeCharacters().Count} of {_roster.Count} duelists (beat the rest in the campaign).";
            _menu.StatsLine = () =>
            {
                var profile = _campaign.Profile;
                return profile == null ? "" : $"{_campaign.Circuits.ClearedCount(profile)}/{_campaign.Circuits.All.Count} circuits cleared · {profile.PacksOpened} pack{(profile.PacksOpened == 1 ? "" : "s")} opened";
            };
            _menu.Showcase = () =>
            {
                // Your rarest card with a picture; before a campaign, any installed card as a Secret Rare.
                var art = CardView.Art;
                if (art == null) return null;
                var profile = _campaign.Profile;
                if (profile != null)
                {
                    var best = profile.Collection.Codes.Where(c => art.Get(c) != null)
                        .OrderByDescending(c => profile.Collection.Best(c)).FirstOrDefault();
                    if (best != 0) return (best, profile.Collection.Best(best) ?? Rarity.Common);
                }
                var installed = art.Installed;
                return installed.Count > 0 ? (installed[new System.Random(seed).Next(installed.Count)], Rarity.SecretRare) : ((uint, Rarity)?)null;
            };
            _tagSetup = gameObject.AddComponent<TagSetupScreen>();
            _tagSetup.Init(_roster, IsFreeCharacter, c => c < 0 ? _allDecks : DeckChoices(_roster[c]));
            _tagSetup.Start += lineup =>
            {
                seed++;
                string Team(int lead, int partner) => $"{(lineup.Characters[lead] < 0 ? "You" : _roster[lineup.Characters[lead]].Name)} & {_roster[lineup.Characters[partner]].Name}";
                CoinToss(Team(0, 1), Team(2, 3), lineup.Decks[0], lineup.Decks[2], () => { _paused = false; NewDuel(null, lineup); UpdateControls(); },
                    youChoose: lineup.YouPlay);
            };
            _tagSetup.Back += () => _menu.Open();
            _menu.Chosen += choice =>
            {
                // Over a campaign duel only the way back, Concede, Settings, About and Quit (the menu shows the rest shut).
                if (CampaignDuelLive && choice is not (MainMenu.Choice.BackToDuel or MainMenu.Choice.Concede or MainMenu.Choice.Settings or MainMenu.Choice.About or MainMenu.Choice.Quit))
                    return;
                // Anything but back to the duel takes the board's place (Free Duel's setup, Tag Duel's, the editor...): a results
                // screen still waiting its 2 s stays away, its scenes queued for the campaign screen (DuelController.Results).
                if (choice is not (MainMenu.Choice.BackToDuel or MainMenu.Choice.Concede)) _duelSerial++;
                switch (choice)
                {
                    case MainMenu.Choice.BackToDuel: ResumeFromMenu(); break;
                    case MainMenu.Choice.Concede: ResumeFromMenu(); ConcedeDuel(); break;
                    case MainMenu.Choice.Campaign: _menu.Close(); _campaignScreen.Open(); break;
                    case MainMenu.Choice.FreeDuel:
                        _menu.Close();
                        _paused = true;
                        UpdateControls();
                        if (_view.DevPanel) _view.AppendLog("Free Duel: pick the matchup on the right and press Start Duel.");
                        else ShowFreeDuel();
                        break;
                    case MainMenu.Choice.TagDuel: _menu.Close(); _tagSetup.Open(); break;
                    case MainMenu.Choice.DeckEditor: _menu.Close(); _editorFromMenu = true; _editor.Open(_pickDeck[0]); break;
                    case MainMenu.Choice.Replays: _menu.Close(); _browserFromMenu = true; _browser.Open(); break;
                    case MainMenu.Choice.Settings: _menu.Close(); _settings.Open(); break;
                    case MainMenu.Choice.About: _menu.Close(); _about.Open(); break;
                    case MainMenu.Choice.ImportCards: _menu.Close(); _import.Open(); break;
                    // Over a campaign duel the save has it live (since its Duel!): the next launch settles it as a loss, as
                    // after Cmd-Q or a crash (CampaignState.LiveDuel). Nothing is recorded here, so it's never counted twice.
                    case MainMenu.Choice.Quit: Application.Quit(); break;
                }
            };

#if ENABLE_INPUT_SYSTEM
            // Game controllers drive a virtual mouse; these are the buttons that aren't clicks.
            _controller = gameObject.AddComponent<ControllerSupport>();
            _controller.Back += ControllerBack;
            _controller.Page += delta => { if (_about.IsOpen) _about.Page(delta); else if (_editor.IsOpen) _editor.ControllerPage(delta); else _view.Prompt.PageBy(delta); };
            // Y: the deck editor's favorite; in a duel, the phase bar on your turn, Skip to my turn on theirs.
            _controller.North += () =>
            {
                if (_editor.IsOpen) _editor.ControllerFavorite();
                else if (_bracketScreen.IsOpen) _bracketScreen.ControllerSkip();
                else if (_human != null && CanSkipTurn()) _view.RequestSkip();
                else if (_human != null) _view.OpenPhasePicker();
            };
            _controller.Pause += () => { if (!_editor.IsOpen) { _paused = !_paused; UpdateControls(); } };
            // View: in the deck editor its More filters panel; in a duel the next explained term.
            _controller.Terms += () => { if (_editor.IsOpen) _editor.ControllerMore(); else _view.NextTerm(); };
#endif
            _view.AppendLog($"Loaded {_cardPack.Count} cards, {_roster.Count} characters, {_allDecks.Count} decks. ocgcore {Duel.CoreVersion}.");
            var art = CardView.Art!;
            _view.AppendLog(art.Folders.Count > 1 || art.Available > 0
                ? $"Card art: {art.Available} cards with pictures in {string.Join(" + ", art.Folders)} (cache: {ArtLoader.CacheFolder})"
                : $"No card art installed (text cards). Add <card id>.jpg files to {art.Folders[0]}");
            if (art.Styles.Count > 0) _view.AppendLog($"Art styles: {string.Join(", ", art.Styles)} (in {art.StylesRoot}).");

            // `-p0 vera -p1 hollis -deck0 warriors -deck1 stall-burn` sets the first matchup; otherwise it's random.
            PickRandom();
            ApplyCommandLinePicks();
            NewDuel();

            // Automated checks: `-autoshot <mid.png>` saves a screenshot mid-duel; `-autoshot-end <end.png>`
            // also waits for the result banner. Both run at high speed and quit afterwards.
            var args = Environment.GetCommandLineArgs();
            string? Arg(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            // `-mute`: no sound this session (tests and QA runs); the volume settings themselves are untouched.
            if (Array.IndexOf(args, "-mute") >= 0) AudioListener.volume = 0f;
            // `-gosecond`: checks that skip the toss give the other side turn 1 (you go second).
            _nextFirstPlayer = _checkFirstPlayer = Array.IndexOf(args, "-gosecond") >= 0 ? 1 : 0;
            var mid = Arg("-autoshot");
            var end = Arg("-autoshot-end");
            if (mid != null || end != null) StartCoroutine(AutoScreenshot(mid, end));
            // `-editor <deck> [-editor-search <query>] [-editor-test]`: open the deck editor (for screenshots / checks).
            var editorDeck = Arg("-editor");
            if (editorDeck != null)
            {
                var opening = System.Diagnostics.Stopwatch.StartNew();
                int decoded = ArtLoader.Decoded, cached = ArtLoader.CacheHits;
                _editor.Open(editorDeck);
                Debug.Log($"editor: opened {editorDeck} in {opening.Elapsed.TotalMilliseconds:0} ms "
                    + $"({ArtLoader.Decoded - decoded} pictures decoded, {ArtLoader.CacheHits - cached} from the disk cache)");
                _editor.DebugSetup(Arg("-editor-search"), Array.IndexOf(args, "-editor-test") >= 0, Array.IndexOf(args, "-editor-hand") >= 0);
                if (Arg("-versioncheck") is string versionFolder) _editor.StartCoroutine(_editor.DebugVersionCheck(versionFolder));
                if (Array.IndexOf(args, "-editor-dragtest") >= 0) _editor.StartCoroutine(_editor.DebugDragTest(holdLastDrag: true));
#if ENABLE_INPUT_SYSTEM
                if (Array.IndexOf(args, "-controllertest") >= 0) StartCoroutine(ControllerTest());
#endif
            }
            // `-foilshowcase <out.png>`: every foil style side by side under a fixed light, screenshotted. Options: `-foilcards
            // id,...`, `-foilrarities r,...`, `-foilall`, `-foillight x,y,hover|drift`, `-foilsweep N`, `-foilstrength s`,
            // `-foilboard`, `-foilslabs` (see FoilShowcase).
            var showcase = Arg("-foilshowcase");
            // `-foilintensity subtle|standard|vivid`: the Foil intensity setting for this run only (screenshot runs otherwise use Standard).
            if (Arg("-foilintensity") is string foilIntensity)
                FxSettings.FoilIntensityOverride = foilIntensity.Trim().ToLowerInvariant() switch { "subtle" => 0, "vivid" => 2, _ => 1 };
            if (showcase != null) FoilShowcase.Run(gameObject, _cardPack!, showcase, FoilShowcase.Options.From(Arg, name => Array.IndexOf(args, name) >= 0));
            // `-gradeshowcase <folder>`: every grade's wear side by side (and the label-only setting), screenshotted under
            // the same fixed light (`-foillight`).
            // `-slabreveal <folder>`: the desk's slab reveal, frame by frame, screenshotted (see SlabShowcase).
            if (Arg("-slabreveal") is string slabReveal) SlabShowcase.RunReveal(gameObject, _cardPack!, slabReveal, FoilShowcase.ParseLight(Arg("-foillight")));
            // `-artcheck <folder>`: card pictures' load times and memory, old way and new (see ArtCheck).
            if (Arg("-artcheck") is string artCheck) ArtCheck.Run(gameObject, artCheck);
            if (Arg("-gradeshowcase") is string gradeShots)
                GradeShowcase.Run(gameObject, _cardPack!, gradeShots, FoilShowcase.ParseLight(Arg("-foillight")),
                    int.TryParse(Arg("-gradesweep"), out var gradeSweep) ? Mathf.Max(0, gradeSweep) : 0);
            if (Array.IndexOf(args, "-reducedmotion") >= 0) FxSettings.ReducedMotionOverride = true;
            // `-foiltilt x,y` outside the foil showcase: every tilting picture (card detail, reading view, cut-in) held at
            // that tilt, for checks that shoot those views.
            if (showcase == null && FoilShowcase.ParseTilt(Arg("-foiltilt")) is Vector2 heldTilt) CardFoil.FixedTilt = heldTilt;
            // `-cardtilt on|off`: the Card tilt setting for this run only.
            if (Arg("-cardtilt") is string cardTilt) FxSettings.CardTiltOverride = !cardTilt.Trim().Equals("off", StringComparison.OrdinalIgnoreCase);
            if (Array.IndexOf(args, "-noplaytabs") >= 0) FxSettings.PlayableTabsOverride = false;
            if (Arg("-textsize") is string textSize && int.TryParse(textSize, out var size)) FxSettings.TextSizeOverride = Mathf.Clamp(size, 0, 2);
            // `-fxshots <folder>`: plays at normal speed and saves a screenshot of each kind of animation, then quits.
            var fxShots = Arg("-fxshots");
            if (fxShots != null) StartCoroutine(FxShots(fxShots));
            // `-replaycheck <folder>`: plays a duel, then checks its auto-saved replay plays back to the same end (screenshots too).
            var replayCheck = Arg("-replaycheck");
            if (replayCheck != null) StartCoroutine(ReplayCheck(replayCheck));
            // `-campaigncheck <folder>`: screenshots of the campaign, shop, a pack opening and collection deck building, then a
            // campaign duel (random clicks) to check it pays out and saves. Use with -mods <empty folder> for a fresh save.
            // `-tagcheck <folder>`: the tag setup screen, a four-bot tag duel (screenshots mid-duel after a swap), its result
            // and its saved replay.
            // `-boardcheck <folder>`: you play with random clicks; screenshots when you choose a zone, and of the Graveyard viewer.
            // `-matcheck <folder>`: two Field Spell decks play; screenshots of the mats and of a Field Spell taking over.
            var matCheck = Arg("-matcheck");
            if (matCheck != null) StartCoroutine(MatCheck(matCheck));
            var boardCheck = Arg("-boardcheck");
            if (boardCheck != null) StartCoroutine(BoardCheck(boardCheck));
            var tagCheck = Arg("-tagcheck");
            if (tagCheck != null) StartCoroutine(TagCheck(tagCheck));
            var campaignCheck = Arg("-campaigncheck");
            if (campaignCheck != null) StartCoroutine(CampaignCheck(campaignCheck));
            // `-finalcheck <folder>`: the Library's "Every Era" final to its epilogue, every story line screenshotted.
            var finalCheck = Arg("-finalcheck");
            if (finalCheck != null) StartCoroutine(FinalCheck(finalCheck));
            // `-laddercheck <folder>`: the Champion's Ladder from its locked plaque to the top (DuelController.LadderCheck).
            if (Arg("-laddercheck") is string ladderCheck) StartCoroutine(LadderCheck(ladderCheck));
            // `-themecheck <folder>`: the shell screens in both UI themes (Main Street, the Back Alley), screenshotted.
            var themeCheck = Arg("-themecheck");
            if (themeCheck != null) StartCoroutine(ThemeCheck(themeCheck));
            // `-shopcheck <folder>` (absolute): the card shop's shelves, tags, odds, a box and New arrivals (DuelController.Shop).
            // `-alleycheck <folder>` (absolute): the Back Alley's Phase A and B loop (DuelController.AlleyCheck).
            if (Arg("-alleycheck") is string alleyCheck) StartCoroutine(AlleyCheck(alleyCheck));
            var shopCheck = Arg("-shopcheck");
            if (shopCheck != null) StartCoroutine(ShopCheck(shopCheck));
            // `-autoclick`: answer your prompts at random through the prompt panel (tests human play end to end).
            if (Array.IndexOf(args, "-autoclick") >= 0) StartCoroutine(AutoClick());
            // `-menucheck <folder>`: screenshots of the title (and attract mode), menu, settings, and campaign start.
            var menuCheck = Arg("-menucheck");
            if (menuCheck != null) StartCoroutine(MenuCheck(menuCheck));
            // `-layoutcheck <folder>`: the play layout through a Free Duel (setup, card detail, gear, history, messages,
            // the developer panel, the result's buttons) and a replay's controls.
            // `-pilecheck <folder>`: a set-up board where piles can be used: the pile menus (View / Summon or Activate /
            // Cancel), the picker and its Cancel, a double-click to view, face-down banished cards, Cancel on card menus.
            var pileCheck = Arg("-pilecheck");
            if (pileCheck != null) StartCoroutine(PileCheck(pileCheck));
            // `-controlscheck <folder>`: the phase bar (what's lit, End, Battle, Main 2 straight from Main 1), the attack
            // arrow (pointing at a target, then the attack itself), and pinning with a right-click or P.
            var controlsCheck = Arg("-controlscheck");
            if (controlsCheck != null) StartCoroutine(ControlsCheck(controlsCheck));
            var layoutCheck = Arg("-layoutcheck");
            if (layoutCheck != null) StartCoroutine(LayoutCheck(layoutCheck));
            // `-qaplay <folder> [-qagames N]`: the campaign played like a player, logging every problem (DuelController.Qa).
            var qaPlay = Arg("-qaplay");
            if (qaPlay != null) StartCoroutine(QaPlay(qaPlay, int.TryParse(Arg("-qagames"), out var qaGames) ? qaGames : 8));
            // `-ordercheck <folder>`: choosing the order of effects that activate at once, and the chain dialog's hint.
            var orderCheck = Arg("-ordercheck");
            if (orderCheck != null) StartCoroutine(OrderCheck(orderCheck));
            var musicCheck = Arg("-musiccheck");
            if (musicCheck != null) StartCoroutine(MusicCheck(musicCheck));
            var qaReplay = Arg("-qareplay");
            if (qaReplay != null) StartCoroutine(QaReplay(qaReplay, Arg("-qaout") ?? Path.GetDirectoryName(qaReplay)!));
            // `-stylecheck <folder>`: card art styles, picked with the Artwork buttons; run twice to check they're kept.
            var styleCheck = Arg("-stylecheck");
            if (styleCheck != null) StartCoroutine(StyleCheck(styleCheck));
            // `-clickcheck <folder>`: clicks that must land the first time (drags in lists, hand cards, a press across a redraw).
            if (Arg("-clickcheck") is string clickCheck) StartCoroutine(ClickCheck(clickCheck));
            // `-ydkcheck <folder> -ydkfile <a .ydk> -ydkfixture <a 40-card .ydk>`: .ydk import from paths (DuelController.YdkCheck).
            if (Arg("-ydkcheck") is string ydkCheck) StartCoroutine(YdkCheck(ydkCheck, Arg("-ydkfile"), Arg("-ydkfixture")));
            // `-searchcheck <folder>` (absolute; with -mods <empty folder>): the deck editor's card search (DuelController.SearchCheck).
            if (Arg("-searchcheck") is string searchCheck) StartCoroutine(SearchCheck(searchCheck));
            // `-aboutcheck <folder> [-aboutlicenses <folder>]` (absolute): the About screen from the menu (DuelController.AboutCheck).
            var aboutCheck = Arg("-aboutcheck");
            if (aboutCheck != null) StartCoroutine(AboutCheck(aboutCheck, Arg("-aboutlicenses")));
            // `-importcheck <folder>` (absolute; with -mods <empty folder>): Import Cards from the menu, with test pictures it
            // makes itself (DuelController.ImportCheck).
            var importCheck = Arg("-importcheck");
            if (importCheck != null) StartCoroutine(ImportCheck(importCheck));

            // Players start on the title screen; automated checks go straight to what they test.
            var checks = new[] { "-autoshot", "-autoshot-end", "-editor", "-fxshots", "-replaycheck", "-campaigncheck", "-finalcheck", "-laddercheck", "-themecheck", "-shopcheck", "-alleycheck", "-tagcheck", "-boardcheck", "-matcheck", "-foilshowcase", "-gradeshowcase", "-slabreveal", "-artcheck", "-autoclick", "-p0", "-p1", "-layoutcheck", "-pilecheck", "-controlscheck", "-qaplay", "-qareplay", "-ordercheck", "-musiccheck", "-stylecheck", "-clickcheck", "-ydkcheck", "-searchcheck", "-aboutcheck", "-importcheck" };
            PromptTiming = checks.Any(c => Array.IndexOf(args, c) >= 0); // checks log decisions left waiting
            // A check that throws stops its coroutine; without this the app would sit there forever. Fail and quit instead.
            if (PromptTiming)
                Application.logMessageReceived += (message, stack, type) =>
                {
                    if (type != LogType.Exception || !stack.Contains("Check>d__")) return;
                    Debug.Log($"check: FAIL the check stopped on an exception: {message}");
                    Application.Quit(1);
                };
            // Checks skip the coin toss (you go first) so their duels stay as they were; QA play tosses like a player.
            _autoToss = PromptTiming && Array.IndexOf(args, "-qaplay") < 0;
            if (menuCheck != null || aboutCheck != null || importCheck != null || !checks.Any(c => Array.IndexOf(args, c) >= 0))
            {
                _paused = true;
                UpdateControls();
                _title.Open();
            }
        }

        private System.Collections.IEnumerator AutoScreenshot(string? midPath, string? endPath)
        {
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            yield return new WaitForSecondsRealtime(6);
            if (midPath != null)
            {
                ScreenCapture.CaptureScreenshot(midPath);
                Debug.Log($"Saved screenshot to {midPath} (turn {_board.Turn})");
            }
            if (endPath != null)
            {
                var deadline = Time.realtimeSinceStartup + 120;
                while (!_announcedResult && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForSecondsRealtime(0.5f);
                ScreenCapture.CaptureScreenshot(endPath);
                Debug.Log($"Saved screenshot to {endPath} (turn {_board.Turn}, finished: {_announcedResult})");
            }
            yield return new WaitForSecondsRealtime(1);
            Application.Quit();
        }

        /// <summary>A campaign duel: you, with your campaign deck, against a circuit match's character and deck.</summary>
        private CampaignScenes _scenes = null!;
        private string _modsRoot = "";
        private Story _story = Story.Empty;
        private StoryScreen _storyScreen = null!;

        /// <summary>
        /// Plays the story scenes these events trigger (if any, and not seen yet), saves, then continues. <paramref name="grants"/>:
        /// the card unlocks of the result, for the scenes' {card} and {set}. Each scene is saved as seen once it's over
        /// (<see cref="SaveScenesAsTheyEnd"/>), so a quit mid-way replays only the scene on screen and what follows, never one
        /// already shown. <paramref name="played"/>: called as the scenes end, before the closing save (that save carries it).
        /// The saves are progress-only (<see cref="CampaignState.SaveProgress"/>): profile.txt.bak stays the last real save's.
        /// </summary>
        private void PlayStory(IEnumerable<string> events, string partnerId, Action then, IReadOnlyList<Grant>? grants = null,
            IReadOnlyDictionary<string, string>? fill = null, Action? played = null)
        {
            var profile = _campaign.Profile;
            if (profile == null) { then(); return; }
            var scenes = _story.Pending(profile, events, partnerId);
            if (scenes.Count == 0) { then(); return; }
            var partner = _roster.FirstOrDefault(p => p.Id == partnerId);
            // A scene over the alley tab or the van (the fence before the van, first-claim...) keeps the rain bed on.
            _storyOverAlley = _campaignScreen.InAlley || _van.IsOpen;
            int run = ++_storyRun;
            _storyScreen.Play(scenes, profile, partner, () => { _storyRun++; played?.Invoke(); _campaign.SaveProgress(); then(); }, grants, fill);
            if (_storyScreen.IsOpen && _storyRun == run) StartCoroutine(SaveScenesAsTheyEnd(scenes, profile, partner?.Id ?? "", run));
        }

        /// <summary>Counts <see cref="PlayStory"/>'s plays (and their ends): a scene saver belongs to one play.</summary>
        private int _storyRun;

        /// <summary>
        /// While one play's scenes are on screen: each time a scene ends (the story screen marks the next one seen as it starts),
        /// the save is written with the finished ones seen and the one now on screen not yet (it's unmarked just for the write).
        /// </summary>
        private System.Collections.IEnumerator SaveScenesAsTheyEnd(List<StoryScene> scenes, Profile profile, string partnerId, int run)
        {
            // Story's seen key: a "partner" scene is once per partner.
            var keys = scenes.Select(s => s.PerPartner ? $"{s.Id}:{partnerId}" : s.Id).ToList();
            int saved = 0;
            while (run == _storyRun && _storyScreen.IsOpen && _campaign.Profile == profile)
            {
                int finished = keys.Count(profile.Seen.Contains) - 1;
                if (finished > saved)
                {
                    var onScreen = keys.Skip(finished).Where(profile.Seen.Remove).ToList();
                    _campaign.SaveProgress();
                    foreach (var k in onScreen) profile.Seen.Add(k);
                    saved = finished;
                }
                yield return null;
            }
        }
        private TraderScreen _trader = null!;
        private RecipeBookScreen _recipeBook = null!;
        private StructureScreen _structures = null!;

        /// <summary>Before a campaign duel: who you face, their deck, and their opening line.</summary>
        /// <summary>The cards a campaign deck may use in the editor (a custom loaner adds its recipe's cards).</summary>
        private Collection EditorCollection(string? deck) =>
            deck != null && _library.Contains(deck) ? _campaign.CollectionFor(_library.Load(deck)) : _campaign.Profile!.Collection;

        /// <summary>The editor from the campaign screen: under the Library cup shown (its list and pool, forced), else the circuit's list.</summary>
        private void OpenCampaignEditor(string? deck)
        {
            // From the alley: the shown table's rules (the No Limits rules from its panel, else the Alley's: its list, no Ban
            // Passes), in its theme; from an Unlisted page opened as a new deck: the Alley's rules.
            if (_campaignScreen.InAlley) { _editor.Open(deck, EditorCollection(deck), _campaign.AlleyRulesFor(_campaignScreen.AlleyTableShown)); return; }
            if (_campaignScreen.UnlistedShown) { _editor.Open(deck, EditorCollection(deck), _campaign.AlleyRules); return; }
            // The Champion's Ladder's board: under the ladder's rules (the last circuit's, as its versus checks).
            if (_campaignScreen.LadderShown) { _editor.Open(deck, EditorCollection(deck), _campaign.LadderRules); return; }
            int shown = _campaignScreen.LibraryCup, cups = _campaign.Library.Cups.Count;
            // The final's panel: under its next round's rules (the first, once it's won).
            if (shown == cups && _campaign.Library.Rounds.Count > 0)
                _editor.Open(deck, EditorCollection(deck), _campaign.RoundRules(Math.Max(0, _campaign.Library.FinalNext(_campaign.Profile!))));
            else if (shown >= 0 && shown < cups) _editor.Open(deck, EditorCollection(deck), _campaign.CupRules(shown));
            else _editor.Open(deck, EditorCollection(deck), _campaign.ListFor(_campaignScreen.Selected)?.Name, _campaign.CircuitTexts(_campaignScreen.Selected));
        }

        /// <summary>The deck picked on the versus screen: one of your campaign decks, or a loaner ("loaner:..."); null: your campaign deck.</summary>
        private string? _versusDeck;

        /// <summary>The cards (base codes) whose originals your deck played as current text in the last duel started (for checks).</summary>
        private List<uint> _textsDowngraded = new List<uint>();

        /// <summary>Decks you can take into a campaign duel: your own campaign decks, then the recipes you can borrow.</summary>
        private List<string> CampaignDeckChoices() =>
            _library.Names.Where(_library.IsCampaign).Concat(_campaign.LoanerDecks()).Distinct().ToList();

        /// <summary>
        /// A loaner's title beside an opponent playing the same deck: named after whose recipe you borrowed ("Ryo's
        /// Classic"), so "Classic (loaner)" and their "Classic" don't read alike. Otherwise just the deck's title.
        /// </summary>
        private string LoanerTitle(string recipe, string theirTitle)
        {
            var title = DeckTitle(recipe);
            if (title != theirTitle || _campaign.Profile == null) return title;
            var owner = RecipeBook.Unlocked(_campaign.Circuits, _campaign.Profile).FirstOrDefault(r => r.Deck == recipe)?.Owner.Name;
            return owner != null ? $"{owner}'s {title}" : title;
        }

        /// <summary>A deck's title on its LP plate (a lent recipe named by <see cref="LoanerTitle"/> against the other side's).</summary>
        private string PlateTitle(Deck deck, Deck other) =>
            deck.Name.EndsWith(" (loaner)") && deck.Note("loaner").Length > 0
                ? $"{LoanerTitle(deck.Note("loaner"), DeckTitle(other.Name))} (loaner)"
                : DeckTitle(deck.Name);

        /// <summary>A deck's name as the versus screen shows it.</summary>
        private string VersusDeckName(string deck) => Loaners.IsLoaner(deck) ? DeckTitle(Loaners.RecipeOf(deck)) : deck;

        /// <summary>Before a circuit match; <paramref name="cycled"/>: shown again after the deck arrows (keep that pick).</summary>
        private void ShowVersus(int circuit, int match, bool cycled = false)
        {
            var c = _campaign.Circuits.All[circuit];
            var m = c.Matches[match];
            var profile = _campaign.Profile!;
            var choices = PickVersusDeck(cycled);
            bool loaner = _campaign.IsLoanerDeck(_versusDeck!);
            var yourDeck = Loaners.IsLoaner(_versusDeck) ? _campaign.Loaner(_versusDeck, circuit) : _library.Load(_versusDeck);
            // A borrowed recipe under a newer ban list: what it lost and gained, so nothing changes behind your back.
            var adjusted = Loaners.IsLoaner(_versusDeck) ? _campaign.LoanerChanges(Loaners.RecipeOf(_versusDeck), circuit) : new List<string>();
            var theirDeck = _library.Contains(m.Deck) ? _library.Load(m.Deck) : new Deck();
            int wins = profile.WinsAgainst(m.Character.Id), losses = profile.Losses.TryGetValue(m.Character.Id, out var l) ? l : 0;
            var list = _campaign.ListFor(circuit);
            var you = new CampaignScenes.Side
            {
                Name = "You", Deck = Loaners.IsLoaner(_versusDeck!) ? LoanerTitle(Loaners.RecipeOf(_versusDeck!), DeckTitle(m.Deck)) : VersusDeckName(_versusDeck!),
                DeckNote = Loaners.IsLoaner(_versusDeck) ? "(loaner · half DP)" : loaner ? "(custom loaner · half DP)" : "",
                Showcase = CampaignScenes.ShowcaseOf(yourDeck, _cardPack!),
                Detail = $"You have {profile.Dp:N0} DP · {profile.Collection.Total} cards in your collection" + (list != null ? $"\nBan list: {list.Name}" : "\nHouse rules (no ban list)")
                         + (adjusted.Count > 0 ? $"\n<color=#E3A21A>Adjusted for it: {string.Join(", ", adjusted.Take(3))}{(adjusted.Count > 3 ? $" (+{adjusted.Count - 3} more)" : "")}</color>" : ""),
            };
            var them = new CampaignScenes.Side
            {
                Name = m.Character.Name, Title = m.Character.Title, Deck = DeckTitle(m.Deck),
                DeckNote = theirDeck.Note("era").Length > 0 ? $"({theirDeck.Note("era")})" : "",
                Showcase = CampaignScenes.ShowcaseOf(theirDeck, _cardPack!), Quote = m.Character.IntroFor(c.Matches.Take(match).Any(earlier => earlier.Character.Id == m.Character.Id)), Portraits = { m.Character.Id },
                Detail = m.Character.StyleSummary + (wins + losses > 0 ? $"\nYour record against them: {wins}-{losses}" : "\nFirst meeting"),
            };
            BossSting(BossMusic.ForMatch(_campaign.Circuits, circuit, match), cycled);
            _scenes.ShowVersus(c.Name, $"Match {match + 1} of {c.Matches.Count}", you, them,
                start: () =>
                {
                    seed++;
                    BeginLiveDuel(LiveDuels.Circuit, circuit, match); // live from here: closing the app now is a loss at the next launch
                    CoinToss("You", m.Character.Name, _versusDeck ?? profile.Deck, m.Deck, () => StartCampaignDuel(circuit, match),
                        measured: MeasuredToss(Profile.MatchKey(circuit, match)));
                },
                back: () => _campaignScreen.Open(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowVersus(circuit, match, cycled: true); },
                scouts: Scouts((m.Character.Name, m.Deck)), stuck: Stuck(Profile.MatchKey(circuit, match), false, new[] { m.Character.Id }, m.Deck), quiet: cycled,
                offer: OwnOffer(Profile.MatchKey(circuit, match), false, new[] { m.Character.Id }, circuit, () => ShowVersus(circuit, match, cycled: true)),
                note: TakeOwnedNote(), texts: _campaign.TextsLine(yourDeck, _campaign.Rules(circuit)));
        }

        /// <summary>The last Stuck? purchase's note, once.</summary>
        private string TakeOwnedNote()
        {
            var note = _ownedNote;
            _ownedNote = "";
            return note;
        }

        /// <summary>A boss's versus screen (not shown again by the deck arrows): their sting, and their theme building.</summary>
        private static void BossSting((string theme, int transpose) music, bool cycled)
        {
            if (!cycled && music.theme != BossMusic.Everyday) Sounds.Sting(music.theme, music.transpose);
        }

        /// <summary>
        /// The deck choices for a versus screen (yours, then loaners), with <see cref="_versusDeck"/> set: your deck as it
        /// stands (picked on the campaign screen, or saved as new); only a loaner picked last time carries over to the next
        /// versus screen, and <paramref name="cycled"/> keeps what the arrows reached.
        /// </summary>
        private List<string> PickVersusDeck(bool cycled)
        {
            var choices = CampaignDeckChoices();
            var profile = _campaign.Profile!;
            if (_versusDeck == null || !choices.Contains(_versusDeck) || (!cycled && !_campaign.IsLoanerDeck(_versusDeck))) _versusDeck = profile.Deck;
            return choices;
        }

        private void CycleVersusDeck(List<string> choices, int delta)
        {
            int i = Math.Max(0, choices.IndexOf(_versusDeck!));
            _versusDeck = choices[(i + delta + choices.Count) % choices.Count];
        }

        /// <summary>The deck picked on the versus screen as it will play under <paramref name="circuit"/>'s rules.</summary>
        /// <summary>A partner's deck as it plays under <paramref name="circuit"/>'s rules (Kit may bring a loaner).</summary>
        private Deck PartnerDeckFor(string name, int circuit) => _campaign.PartnerPlays(name, circuit);

        /// <summary>A loaner on your team (your deck or your partner's, in slots 0 and 1): the win pays half DP.</summary>
        private bool TeamHasLoaner(TagLineup? tag, string yourDeck) =>
            _campaign.IsLoanerDeck(tag != null ? tag.Decks[0] : yourDeck) || (tag != null && _campaign.IsLoanerDeck(tag.Decks[1]));

        private Deck YourVersusDeck(int circuit) =>
            Loaners.IsLoaner(_versusDeck!) ? _campaign.Loaner(_versusDeck!, circuit) : _library.Contains(_versusDeck!) ? _library.Load(_versusDeck!) : new Deck();

        /// <summary>The note under your deck: a loaner of yours, or (in a tag duel) your partner's, pays half DP.</summary>
        private string VersusDeckNote(string partnerDeck = "") =>
            Loaners.IsLoaner(_versusDeck!) ? "(loaner · half DP)" : _campaign.IsLoanerDeck(_versusDeck!) ? "(custom loaner · half DP)"
            : partnerDeck.Length > 0 && _campaign.IsLoanerDeck(partnerDeck) ? "(your partner's loaner · half DP)" : "";

        /// <summary>What a borrowed recipe lost and gained under <paramref name="circuit"/>'s list, as a line for the versus screen.</summary>
        private string LoanerAdjustments(int circuit)
        {
            var adjusted = Loaners.IsLoaner(_versusDeck!) ? _campaign.LoanerChanges(Loaners.RecipeOf(_versusDeck!), circuit) : new List<string>();
            return adjusted.Count > 0 ? $"\n<color=#E3A21A>Adjusted for it: {string.Join(", ", adjusted.Take(3))}{(adjusted.Count > 3 ? $" (+{adjusted.Count - 3} more)" : "")}</color>" : "";
        }

        /// <summary>What the list changed in your partner's deck (era-limited play), as a line for a tag versus screen.</summary>
        private string PartnerAdjustments(string partnerName, string deck, int circuit)
        {
            var adjusted = _campaign.PartnerChanges(deck, circuit);
            return adjusted.Count > 0 ? $"\n<color=#E3A21A>{partnerName}'s deck, adjusted for it: {string.Join(", ", adjusted.Take(2))}{(adjusted.Count > 2 ? $" (+{adjusted.Count - 2} more)" : "")}</color>" : "";
        }

        /// <summary>
        /// The deck to play from the versus screen: a loaner just for this duel, or one of yours (which becomes your deck).
        /// </summary>
        private string TakeVersusDeck()
        {
            var picked = _versusDeck ?? _campaign.Profile!.Deck;
            if (!_campaign.IsLoanerDeck(picked) && picked != _campaign.Profile!.Deck) { _campaign.Profile!.Deck = picked; _campaign.Save(); }
            return picked;
        }

        /// <summary>The circuit whose ban list a loaner follows in a Tag Cup or guest duel (circuit matches use their own).</summary>
        private int _loanerCircuit = -1;

        /// <summary>Before a Tag Cup match: your team against theirs.</summary>
        private void ShowTagVersus(int cup, int match, string partnerId, bool cycled = false)
        {
            var c = _campaign.Circuits.TagCups[cup];
            var (lead, second) = c.Matches[match];
            var partner = _roster.First(p => p.Id == partnerId);
            var profile = _campaign.Profile!;
            Deck Load(string name) => _library.Contains(name) ? _library.Load(name) : new Deck();
            // Your deck, as before a circuit match: one of yours, or a loaner (made legal for the cup's ban list, half DP).
            var choices = PickVersusDeck(cycled);
            var mine = YourVersusDeck(c.After);
            var partnerDeckName = _campaign.PartnerDeck(partner);
            var partnerDeck = PartnerDeckFor(partnerDeckName, c.After);
            var you = new CampaignScenes.Side
            {
                Name = $"You & {partner.Name}", Title = partner.Title.Length > 0 ? $"with {partner.DisplayName}" : "", Deck = $"{VersusDeckName(_versusDeck!)} + {DeckTitle(partnerDeckName)}",
                DeckNote = VersusDeckNote(partnerDeckName),
                Showcase = TeamShowcase(mine, partnerDeck), Portraits = { partner.Id },
                Detail = "Tag duel: one field and 8,000 LP per team.\nThe teams take turns: one of you, one of them, the other of you, the other of them." + LoanerAdjustments(c.After)
                         + PartnerAdjustments(partner.Name, partnerDeckName, c.After), Quote = PartnerQuote(partner, partnerDeckName),
            };
            var them = new CampaignScenes.Side
            {
                Name = $"{lead.Character.Name} & {second.Character.Name}", Deck = $"{DeckTitle(lead.Deck)} + {DeckTitle(second.Deck)}",
                Showcase = TeamShowcase(Load(lead.Deck), Load(second.Deck)), Portraits = { lead.Character.Id, second.Character.Id },
                Detail = $"{lead.Character.Name}: {lead.Character.StyleSummary}\n{second.Character.Name}: {second.Character.StyleSummary}", Quote = lead.Character.Intro,
            };
            BossSting(BossMusic.ForTagCup(_campaign.Circuits, cup, match), cycled);
            _scenes.ShowVersus(c.Name, $"Tag match {match + 1} of {c.Matches.Count}", you, them,
                start: () =>
                {
                    seed++;
                    BeginLiveDuel(LiveDuels.TagCup, cup, match, partnerDeck: partnerDeckName);
                    CoinToss($"You & {partner.Name}", $"{lead.Character.Name} & {second.Character.Name}", _versusDeck!, lead.Deck, () => StartCampaignTag(cup, match, partnerId),
                        tag: true, measured: MeasuredToss(Circuits.TagKey(cup, match), partnerId, partnerDeckName));
                },
                back: () => _campaignScreen.Open(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowTagVersus(cup, match, partnerId, cycled: true); },
                scouts: Scouts((lead.Character.Name, lead.Deck), (second.Character.Name, second.Deck)),
                stuck: Stuck(Circuits.TagKey(cup, match), true, new[] { lead.Character.Id, second.Character.Id }, lead.Deck, second.Deck), quiet: cycled,
                offer: OwnOffer(Circuits.TagKey(cup, match), true, new[] { lead.Character.Id, second.Character.Id }, c.After,
                    () => ShowTagVersus(cup, match, partnerId, cycled: true)),
                note: TakeOwnedNote(), texts: _campaign.TextsLine(mine, _campaign.Rules(c.After), partnerDeckName));
        }

        /// <summary>
        /// A partner's line on the tag versus screen: their intro, or for Kit bringing a deck that isn't her own (yours, or
        /// a recipe you lent her), a line about piloting it.
        /// </summary>
        private string PartnerQuote(Personality partner, string deck) =>
            TagPartners.IsShopRegular(partner) && !partner.Decks.Contains(deck)
                ? $"{_campaign.SpokenTitle(deck)}? Good list. Your deck, my hands."
                : partner.Intro;

        /// <summary>Three cards for a team: two from the lead's deck and one from the partner's, never the same card twice.</summary>
        private List<uint> TeamShowcase(Deck lead, Deck partner)
        {
            var cards = CampaignScenes.ShowcaseOf(lead, _cardPack!).Take(2).ToList();
            cards.AddRange(CampaignScenes.ShowcaseOf(partner, _cardPack!).Where(c => !cards.Contains(c)).Take(3 - cards.Count));
            return cards;
        }

        private void StartCampaignTag(int cup, int match, string partnerId)
        {
            var (lead, second) = _campaign.Circuits.TagCups[cup].Matches[match];
            var partner = _roster.FindIndex(p => p.Id == partnerId);
            // The deck they bring and how sharply they play follow your bond with them (TagPartners).
            var partnerDeck = _campaign.PartnerDeck(_roster[partner]);
            _loanerCircuit = _campaign.Circuits.TagCups[cup].After;
            var lineup = new TagLineup
            {
                Characters = new[] { -1, partner, _roster.FindIndex(p => p.Id == lead.Character.Id), _roster.FindIndex(p => p.Id == second.Character.Id) },
                Decks = new[] { TakeVersusDeck(), partnerDeck.Length > 0 ? partnerDeck : OwnDecks(_roster[partner])[0], lead.Deck, second.Deck },
                PartnerStyle = TagPartners.Playing(_roster[partner], _campaign.Profile!),
            };
            _paused = false;
            NewDuel(null, lineup, (cup, match, partnerId));
            UpdateControls();
        }

        /// <summary>After a Tag Cup match: its scenes queued, then the results in 2 s (DuelController.Results).</summary>
        private void BeginTagResults(int cup, int match, DuelReward reward, int winner, string partner)
        {
            var events = reward.LevelUps.Select(u => "level:" + u.Level).ToList();
            if (reward.Summary.Contains("cleared")) events.Add("cupclear:" + _campaign.Circuits.TagCups[cup].Name);
            AddGrantEvents(events, reward);
            ShowResultsSoon(events, reward.Grants, story => ShowTagResults(cup, match, reward, winner, partner, story), partner);
        }

        private void ShowTagResults(int cup, int match, DuelReward reward, int winner, string partner, List<CampaignState.QueuedStory> story)
        {
            var circuits = _campaign.Circuits;
            var c = circuits.TagCups[cup];
            var (lead, second) = c.Matches[match];
            var profile = _campaign.Profile!;
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? "Victory!" : "Defeat",
                Speaker = lead.Character.Name,
                Quote = winner == 0 ? lead.Character.LoseLine : winner == 1 ? lead.Character.WinLine : "",
                Circuit = $"{c.Name} · Tag match {match + 1} against {lead.Character.Name} & {second.Character.Name}",
                TotalDp = profile.Dp,
                Progress = Enumerable.Range(0, c.Matches.Count).Select(m => profile.MatchWins.ContainsKey(Circuits.TagKey(cup, m))).ToList(),
            };
            if (winner == 0)
            {
                int prize = reward.Summary.Contains("cleared") ? c.Prize : 0;
                if (TeamHasLoaner(_tag, ""))
                    // A loaner pays half the duel's DP (the prize in full): what was paid, not the cup's rates.
                    r.Earnings.Add((reward.FirstWin ? "Win and first win (half: loaner deck)" : "Win (half: loaner deck)", reward.Dp - prize));
                else
                {
                    r.Earnings.Add(("Win", circuits.WinDp(c.After)));
                    if (reward.FirstWin) r.Earnings.Add(("First win", Circuits.FirstWinBonus));
                }
                if (prize > 0) r.Earnings.Add(($"{c.Name} prize", prize));
            }
            else r.Earnings.Add((winner == 1 ? "For the duel" : "Draw", reward.Dp));
            foreach (var up in reward.LevelUps.Where(u => u.Dp > 0)) r.Earnings.Add(($"Level {up.Level} reward", up.Dp));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (reward.Exp, profile.Level, into, needed);
            r.Record = $"Tag Cups cleared: {Enumerable.Range(0, circuits.TagCups.Count).Count(t => circuits.IsTagCupCleared(t, profile))} of {circuits.TagCups.Count}";
            foreach (var up in reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : ""));
            if (reward.Summary.Contains("cleared")) r.Unlocks.Add($"{c.Name} cleared!" + (reward.Tokens > 0 ? $" +{reward.Tokens} Card Token" : ""));
            if (winner == 0)
                foreach (var recipe in RecipeBook.JustUnlockedTag(_campaign.Circuits, _campaign.Profile!, cup, match))
                    r.Unlocks.Add($"Recipe unlocked: {recipe.Owner.Name}'s {DeckTitle(recipe.Deck)}. Find it in the Recipe Book.");
            AddGrants(r, reward);
            _scenes.LastResults = r;
            _scenes.ShowResults(r,
                continueAction: () => PlayResultStory(story, () => _campaignScreen.Open(reward.Summary)),
                rematch: Requeued(story, () => ShowTagVersus(cup, match, partner)),
                shop: Requeued(story, () => _shop.Open()));
        }

        private void StartCampaignDuel(int circuit, int match)
        {
            var m = _campaign.Circuits.All[circuit].Matches[match];
            _pickCharacter[0] = -1;
            _pickDeck[0] = TakeVersusDeck();
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == m.Character.Id));
            _pickDeck[1] = _allDecks.Contains(m.Deck) ? m.Deck : OwnDecks(m.Character)[0];
            ShowPicks();
            _paused = false;
            NewDuel((circuit, match));
        }

        // ---- The Format Library: a cup match, like a circuit's under the cup's own rules ----------------------------------

        /// <summary>The Library cup match being played: cup and match (never a circuit index: cups aren't in Circuits.All).</summary>
        private (int cup, int match)? _campaignCup;

        /// <summary>
        /// Before a Library cup match: your decks and the loaners, each loaner made legal for the cup's list and pool. Your own
        /// deck has to be legal here to start (a loaner is picked for you when it isn't); the Stuck? advice and the toss tip
        /// read the cup's measurements (campaign/library-advice.txt).
        /// </summary>
        private void ShowCupVersus(int cup, int match, bool cycled = false)
        {
            var c = _campaign.Library.Cups[cup];
            var m = c.Matches[match];
            var profile = _campaign.Profile!;
            var rules = _campaign.CupRules(cup);
            var adviceKey = Profile.MatchKey(cup, match);
            var streakKey = _campaign.Library.Key(cup, match);
            var measured = _campaign.LibraryAdvice.For(adviceKey);
            var choices = PickVersusDeck(cycled);
            // Your deck isn't legal under this cup's list: start on what was measured here, or the first loaner.
            if (!cycled && !Loaners.IsLoaner(_versusDeck!) && OwnDeckProblems(_versusDeck!, rules).Count > 0)
            {
                var lend = MeasuredPicks(measured, false, m.Character.Id).FirstOrDefault(p => p.Option.Borrows)?.Deck ?? _campaign.LoanerDecks().FirstOrDefault();
                if (lend != null && choices.Contains(lend)) _versusDeck = lend;
            }
            bool loaner = _campaign.IsLoanerDeck(_versusDeck!);
            var yourDeck = Loaners.IsLoaner(_versusDeck!) ? _campaign.Loaner(_versusDeck!, rules) : _library.Contains(_versusDeck!) ? _library.Load(_versusDeck!) : new Deck();
            // A borrowed recipe under the cup's list and pool: what it lost and gained, so nothing changes behind your back.
            var adjusted = Loaners.IsLoaner(_versusDeck!) ? _campaign.LoanerChanges(Loaners.RecipeOf(_versusDeck!), rules) : new List<string>();
            var theirDeck = _library.Contains(m.Deck) ? _library.Load(m.Deck) : new Deck();
            int wins = profile.WinsAgainst(m.Character.Id), losses = profile.Losses.TryGetValue(m.Character.Id, out var l) ? l : 0;
            var you = new CampaignScenes.Side
            {
                Name = "You", Deck = Loaners.IsLoaner(_versusDeck!) ? LoanerTitle(Loaners.RecipeOf(_versusDeck!), DeckTitle(m.Deck)) : VersusDeckName(_versusDeck!),
                DeckNote = Loaners.IsLoaner(_versusDeck!) ? "(loaner · half DP)" : loaner ? "(custom loaner · half DP)" : "",
                Showcase = CampaignScenes.ShowcaseOf(yourDeck, _cardPack!),
                Detail = $"You have {profile.Dp:N0} DP · {profile.Collection.Total} cards in your collection"
                         + (rules.List != null ? $"\nBan list: {rules.List.Name}" : $"\nBan list: {c.Banlist} (not installed: no list)") + $" · cards out by {CampaignScreen.PoolMonth(c.Pool)}"
                         + (adjusted.Count > 0 ? $"\n<color=#E3A21A>Adjusted for it: {string.Join(", ", adjusted.Take(3))}{(adjusted.Count > 3 ? $" (+{adjusted.Count - 3} more)" : "")}</color>" : ""),
            };
            var them = new CampaignScenes.Side
            {
                Name = m.Character.Name, Title = m.Character.Title, Deck = DeckTitle(m.Deck),
                DeckNote = theirDeck.Note("era").Length > 0 ? $"({theirDeck.Note("era")})" : "",
                Showcase = CampaignScenes.ShowcaseOf(theirDeck, _cardPack!), Quote = m.Character.IntroFor(c.Matches.Take(match).Any(earlier => earlier.Character.Id == m.Character.Id)), Portraits = { m.Character.Id },
                Detail = m.Character.StyleSummary + (wins + losses > 0 ? $"\nYour record against them: {wins}-{losses}" : "\nFirst meeting"),
            };
            BossSting(CupMusic(cup, match), cycled);
            _scenes.ShowVersus(c.Name, m.Champion ? "Champion's table (optional)" : $"Match {match + 1} of {c.Matches.Count}", you, them,
                start: () =>
                {
                    // A deck of yours has to be legal under the cup's list and pool (a loaner already is).
                    if (!Loaners.IsLoaner(_versusDeck!) && OwnDeckProblems(_versusDeck ?? profile.Deck, rules) is { Count: > 0 } problems)
                    {
                        _ownedNote = $"<color=#FF8A8A>{VersusDeckName(_versusDeck ?? profile.Deck)} isn't legal in this cup:</color> {problems[0].Message} Pick a loaner with the arrows, or edit your deck.";
                        ShowCupVersus(cup, match, cycled: true);
                        return;
                    }
                    seed++;
                    BeginLiveDuel(LiveDuels.Cup, cup, match);
                    CoinToss("You", m.Character.Name, _versusDeck ?? profile.Deck, m.Deck, () => StartCupDuel(cup, match), measured: MeasuredToss(measured));
                },
                back: () => _campaignScreen.Open(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowCupVersus(cup, match, cycled: true); },
                scouts: Scouts((m.Character.Name, m.Deck)), stuck: Stuck(streakKey, measured, false, new[] { m.Character.Id }, m.Deck), quiet: cycled,
                offer: OwnOffer(streakKey, measured, false, new[] { m.Character.Id }, rules, () => ShowCupVersus(cup, match, cycled: true)),
                note: TakeOwnedNote(), texts: _campaign.TextsLine(yourDeck, rules));
        }

        /// <summary>A deck of yours under a cup's rules: its list, its pool, and cards you don't own (none for a missing deck: the screen said).</summary>
        private List<DeckProblem> OwnDeckProblems(string deck, FormatRules rules) =>
            _library.Contains(deck) ? _campaign.Problems(_library.Load(deck), rules)
                : new List<DeckProblem> { new DeckProblem { Message = "Build a deck first (New deck)." } };

        /// <summary>
        /// A Library cup's music: its boss (the last match before a Champion's table) plays the Master theme, a key for each
        /// cup; the Champion's table the Gauntlet's. Everything else the everyday music.
        /// </summary>
        private (string theme, int transpose) CupMusic(int cup, int match)
        {
            var c = _campaign.Library.Cups[cup];
            if (c.Matches[match].Champion) return ("gauntlet", 0);
            return match == c.Matches.FindLastIndex(m => !m.Champion) ? ("master", CupKeys[cup % CupKeys.Length]) : (BossMusic.Everyday, 0);
        }

        /// <summary>The keys the cups' bosses share the Master theme in (as <see cref="BossMusic"/> spreads a band's).</summary>
        private static readonly int[] CupKeys = { 0, 3, -2, 5 };

        private void StartCupDuel(int cup, int match)
        {
            var m = _campaign.Library.Cups[cup].Matches[match];
            _pickCharacter[0] = -1;
            _pickDeck[0] = TakeVersusDeck();
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == m.Character.Id));
            _pickDeck[1] = _allDecks.Contains(m.Deck) ? m.Deck : OwnDecks(m.Character)[0];
            ShowPicks();
            _paused = false;
            NewDuel(campaignCup: (cup, match));
        }

        // ---- The "Every Era" final ---------------------------------------------------------------------------------------

        /// <summary>The final round being played (null when it isn't one).</summary>
        private int? _campaignRound;

        /// <summary>What was measured against a round's boss at its cup (the toss tip, Make it yours).</summary>
        private List<MeasuredOption> RoundMeasured(int round)
        {
            var r = _campaign.Library.Rounds[round];
            var matches = _campaign.Library.Cups[r.Cup].Matches;
            int at = matches.FindIndex(m => m.Character.Id == r.Match.Character.Id && m.Deck == r.Match.Deck);
            return at < 0 ? new List<MeasuredOption>() : _campaign.LibraryAdvice.For(Profile.MatchKey(r.Cup, at)).ToList();
        }

        /// <summary>A final round's music: the Master theme, a semitone higher each round, as the Gauntlet climbs.</summary>
        private (string theme, int transpose) RoundMusic(int round) => ("master", round);

        /// <summary>
        /// Before an "Every Era" round: only your own decks (no loaners), and one has to be legal under the round's era
        /// list and pool with no Ban Passes to start. The arrows cycle your decks; Make it yours can build the measured one.
        /// </summary>
        private void ShowRoundVersus(int round, bool cycled = false)
        {
            var library = _campaign.Library;
            var r = library.Rounds[round];
            var c = library.Cups[r.Cup];
            var m = r.Match;
            var profile = _campaign.Profile!;
            var rules = _campaign.RoundRules(round);
            var streakKey = library.FinalKey(round);
            var measured = RoundMeasured(round);
            // Decks you built: no loaners, and not your edit of a partner's deck (that's theirs to bring). One check decides
            // what can play a round, here, at its start and on the final's panel (CampaignState.RoundProblems).
            List<DeckProblem> RoundProblems(string d) => _campaign.RoundProblems(d, round);
            var choices = PickVersusDeck(cycled).Where(_campaign.IsYours).ToList();
            if (choices.Count == 0) choices.Add(profile.Deck);
            // Not a deck of yours, or not legal here: start on the first of yours that is (else your deck, to show why not).
            if (!choices.Contains(_versusDeck!) || (!cycled && RoundProblems(_versusDeck!).Count > 0))
                _versusDeck = choices.FirstOrDefault(d => RoundProblems(d).Count == 0) ?? (choices.Contains(profile.Deck) ? profile.Deck : choices[0]);
            var yourDeck = _library.Contains(_versusDeck!) ? _library.Load(_versusDeck!) : new Deck();
            var theirDeck = _library.Contains(m.Deck) ? _library.Load(m.Deck) : new Deck();
            int wins = profile.WinsAgainst(m.Character.Id), losses = profile.Losses.TryGetValue(m.Character.Id, out var l) ? l : 0;
            var problems = RoundProblems(_versusDeck!);
            bool noLegalDeck = choices.All(d => RoundProblems(d).Count > 0);
            var you = new CampaignScenes.Side
            {
                Name = "You", Deck = VersusDeckName(_versusDeck!), DeckNote = problems.Count > 0 ? "(not legal here)" : "",
                Showcase = CampaignScenes.ShowcaseOf(yourDeck, _cardPack!),
                Detail = $"Your own decks only: no loaners, no Ban Passes"
                         + (rules.List != null ? $"\nBan list: {rules.List.Name}" : $"\nBan list: {c.Banlist} (not installed: no list)") + $" · cards out by {CampaignScreen.PoolMonth(c.Pool)}",
            };
            var them = new CampaignScenes.Side
            {
                Name = m.Character.Name, Title = m.Character.Title, Deck = DeckTitle(m.Deck),
                DeckNote = $"({c.Era} · {c.Name})",
                Showcase = CampaignScenes.ShowcaseOf(theirDeck, _cardPack!), Quote = m.Character.IntroFor(true), Portraits = { m.Character.Id },
                Detail = m.Character.StyleSummary + (wins + losses > 0 ? $"\nYour record against them: {wins}-{losses}" : "\nFirst meeting"),
            };
            BossSting(RoundMusic(round), cycled);
            _scenes.ShowVersus(library.Final.Name, $"Round {round + 1} of {library.Rounds.Count} · {c.Era}", you, them,
                start: () =>
                {
                    if (RoundProblems(_versusDeck ?? profile.Deck) is { Count: > 0 } refused)
                    {
                        var why = refused[0].Message;
                        _ownedNote = $"<color=#FF8A8A>{VersusDeckName(_versusDeck ?? profile.Deck)} can't play this round:</color> <color=#F0E6D8>{why}</color>";
                        ShowRoundVersus(round, cycled: true);
                        return;
                    }
                    seed++;
                    BeginLiveDuel(LiveDuels.Round, round);
                    CoinToss("You", m.Character.Name, _versusDeck ?? profile.Deck, m.Deck, () => StartRoundDuel(round), measured: MeasuredToss(measured));
                },
                back: () => _campaignScreen.Open(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowRoundVersus(round, cycled: true); },
                scouts: Scouts((m.Character.Name, m.Deck)), quiet: cycled,
                // None of your decks can play this round: say so at once, with Make it yours for the measured deck
                // (there's no losing three times to a round you can't start).
                stuck: noLegalDeck ? new List<string>
                {
                    $"None of your decks is legal for this round: the {c.Banlist} list, cards out by {CampaignScreen.PoolMonth(c.Pool)}.",
                    "Make it yours buys the cards you're missing and saves the measured deck as your own.",
                } : null,
                offer: OwnOffer(streakKey, measured, false, new[] { m.Character.Id }, rules, () => ShowRoundVersus(round, cycled: true), now: noLegalDeck),
                note: TakeOwnedNote(), arrowsHint: "Arrows: switch between your own decks (no loaners in the final).", texts: _campaign.TextsLine(yourDeck, rules));
        }

        private void StartRoundDuel(int round)
        {
            // The board's Rematch comes straight here: the deck may have changed since its versus screen, so check again.
            var deck = _versusDeck ?? _campaign.Profile!.Deck;
            if (_campaign.RoundProblems(deck, round).Count > 0)
            {
                ShowRoundVersus(round, cycled: true);
                return;
            }
            var m = _campaign.Library.Rounds[round].Match;
            _pickCharacter[0] = -1;
            _pickDeck[0] = TakeVersusDeck();
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == m.Character.Id));
            _pickDeck[1] = _allDecks.Contains(m.Deck) ? m.Deck : OwnDecks(m.Character)[0];
            ShowPicks();
            _paused = false;
            NewDuel(campaignRound: round);
        }

        /// <summary>After an "Every Era" round: its scenes queued, then the board for a moment and the results (DuelController.Results).</summary>
        private void BeginRoundResults(int round, DuelReward reward, int winner)
        {
            var events = Library.Events(reward);
            AddGrantEvents(events, reward);
            ShowResultsSoon(events, reward.Grants, story => ShowRoundResults(round, reward, winner, story));
        }

        /// <summary>An "Every Era" round's results (the round, the final's prize), then its story.</summary>
        private void ShowRoundResults(int round, DuelReward reward, int winner, List<CampaignState.QueuedStory> story)
        {
            var library = _campaign.Library;
            var rd = library.Rounds[round];
            var c = library.Cups[rd.Cup];
            var opponent = rd.Match.Character;
            var profile = _campaign.Profile!;
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? "Victory!" : "Defeat",
                Speaker = opponent.Name,
                Quote = winner == 0 ? opponent.LoseLine : winner == 1 ? opponent.WinLine : "",
                Circuit = $"{library.Final.Name} · Round {round + 1}, {c.Era}, against {opponent.DisplayName}",
                TotalDp = profile.Dp,
                Progress = Enumerable.Range(0, library.Rounds.Count).Select(k => library.FinalWon(profile, k)).ToList(),
                ProgressItem = "Round", ProgressTitle = "FINAL PROGRESS",
            };
            if (winner == 0)
            {
                r.Earnings.Add(("Win", reward.FirstWin ? Library.WinDp : Library.RepeatWinDp));
                if (reward.FirstWin) r.Earnings.Add(("First-win bonus", Circuits.FirstWinBonus));
                if (reward.ClearedCircuit != null) r.Earnings.Add(($"{library.Final.Name} prize", library.Final.Prize));
            }
            else r.Earnings.Add((winner == 1 ? "For the duel" : "Draw", reward.Dp));
            foreach (var up in reward.LevelUps.Where(u => u.Dp > 0)) r.Earnings.Add(($"Level {up.Level} reward", up.Dp));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (reward.Exp, profile.Level, into, needed);
            int wins = profile.WinsAgainst(opponent.Id), losses = profile.Losses.TryGetValue(opponent.Id, out var l) ? l : 0;
            int roundsWon = Enumerable.Range(0, library.Rounds.Count).Count(k => library.FinalWon(profile, k));
            r.Record = $"Against {opponent.Name}: <b>{wins}</b> win{(wins == 1 ? "" : "s")}, <b>{losses}</b> loss{(losses == 1 ? "" : "es")}\n"
                       + $"{library.Final.Name}: {roundsWon} of {library.Rounds.Count} rounds won";
            foreach (var up in reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}: trade {(up.Tokens > 1 ? "them" : "it")} for any card at the Card Trader" : ""));
            if (reward.ClearedCircuit != null)
            {
                r.Unlocks.Add($"{library.Final.Name} won! +{Library.FinalTokens} Card Tokens: trade for any card at the Card Trader");
                if (Posters.Find("every-era") is Poster poster) r.Unlocks.Add($"New poster: {poster.Title}. Hang it on your plate in the Locker");
            }
            else if (reward.RoundOpened > 0)
            {
                var next = library.Rounds[reward.RoundOpened - 1];
                r.Unlocks.Add($"Round {reward.RoundOpened} is next: {next.Match.Character.Name}, {library.Cups[next.Cup].Era} ({library.Cups[next.Cup].Name})");
            }
            else if (winner != 0) r.Unlocks.Add("A lost round is just played again: your other rounds stay won");
            AddGrants(r, reward);
            _scenes.LastResults = r;
            _scenes.ShowResults(r,
                continueAction: () => PlayResultStory(story, () => _campaignScreen.Open(reward.Summary)),
                rematch: Requeued(story, () => ShowRoundVersus(round)),
                shop: Requeued(story, () => _shop.Open()));
        }

        /// <summary>
        /// After a Library cup match, as after a circuit's: its scenes queued (the first cup clear opens the Back Alley now:
        /// its rumor plays after the cup's own scenes), then the board for a moment and the results (DuelController.Results).
        /// </summary>
        private void BeginCupResults(int cup, int match, DuelReward reward, int winner, bool lent, bool built, bool newLoaner)
        {
            var profile = _campaign.Profile!;
            bool alleyOpened = reward.ClearedCircuit != null && _campaign.AlleyReady && BackAlley.Open(profile, _campaign.Library);
            if (alleyOpened) _campaign.Save();
            var events = Library.Events(reward);
            if (alleyOpened) events.Add(BackAlley.OpenEvent);
            _lastCupEvents = events.ToList();
            AddGrantEvents(events, reward);
            ShowResultsSoon(events, reward.Grants, story => ShowCupResults(cup, match, reward, winner, lent, built, newLoaner, alleyOpened, story));
        }

        /// <summary>A Library cup match's results (what the win paid, the cup's clear and stamp), then the story the result raised.</summary>
        private void ShowCupResults(int cup, int match, DuelReward reward, int winner, bool lent, bool built, bool newLoaner, bool alleyOpened,
            List<CampaignState.QueuedStory> story)
        {
            var library = _campaign.Library;
            var c = library.Cups[cup];
            var m = c.Matches[match];
            var opponent = m.Character;
            var profile = _campaign.Profile!;
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? "Victory!" : "Defeat",
                Speaker = opponent.Name,
                Quote = winner == 0 ? opponent.LoseLine : winner == 1 ? opponent.WinLine : "",
                Circuit = $"{c.Name} · {(m.Champion ? "Champion's table" : $"Match {match + 1}")} against {opponent.DisplayName}",
                TotalDp = profile.Dp,
                Progress = Enumerable.Range(0, c.Matches.Count).Select(k => library.HasWon(profile, cup, k)).ToList(),
                ProgressTitle = "CUP PROGRESS",
            };
            if (winner == 0)
            {
                int prize = reward.ClearedCircuit != null ? c.Prize : 0;
                if (lent)
                    // A loaner pays half the duel's DP (the prize in full): what was paid, not the cup's rates.
                    r.Earnings.Add((reward.FirstWin ? "Win and first win (half: loaner deck)" : "Win (half: loaner deck)", reward.Dp - prize));
                else
                {
                    r.Earnings.Add(("Win", reward.FirstWin ? Library.WinDp : Library.RepeatWinDp));
                    if (reward.FirstWin) r.Earnings.Add(("First-win bonus", Circuits.FirstWinBonus));
                }
                if (prize > 0) r.Earnings.Add(($"{c.Name} prize", prize));
            }
            else r.Earnings.Add((winner == 1 ? "For the duel" : "Draw", reward.Dp));
            foreach (var up in reward.LevelUps.Where(u => u.Dp > 0)) r.Earnings.Add(($"Level {up.Level} reward", up.Dp));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (reward.Exp, profile.Level, into, needed);
            int wins = profile.WinsAgainst(opponent.Id), losses = profile.Losses.TryGetValue(opponent.Id, out var l) ? l : 0, stamps = library.StampCount(profile);
            r.Record = $"Against {opponent.Name}: <b>{wins}</b> win{(wins == 1 ? "" : "s")}, <b>{losses}</b> loss{(losses == 1 ? "" : "es")}\n"
                       + $"Format Library: {library.ClearedCount(profile)} of {library.Cups.Count} cups cleared · {stamps} stamp{(stamps == 1 ? "" : "s")}";
            foreach (var up in reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}: trade {(up.Tokens > 1 ? "them" : "it")} for any card at the Card Trader" : ""));
            if (newLoaner) r.Unlocks.Add($"Loaner unlocked: {opponent.Name}'s {DeckTitle(m.Deck)}. Borrow it with the arrows on any versus screen");
            if (reward.UnlockedCircuit != null)
                r.Unlocks.Add($"Every cup cleared: the {library.Final.Name} final is open. Duchess Ilse's six rounds wait on the Library shelf");
            if (reward.ClearedCircuit != null)
            {
                r.Unlocks.Add($"{c.Name} cleared! +{Library.ClearTokens} Card Tokens: trade for any card at the Card Trader");
                r.Unlocks.Add($"On the cup's panel now: the {c.Era} era mat for your side of the board ({Cosmetics.MatPrice:N0} DP)"
                    + (Cosmetics.EngraveDecks(profile, library, cup).Count > 0
                        ? $", and an engraved plate for its binder ({Cosmetics.EngravePrice:N0} DP)"
                        : $"; win a match here with a deck of yours to engrave its binder"));
            }
            if (reward.StampedCup != null)
                r.Unlocks.Add($"★ Format Master stamp: {c.Name}! +{Library.StampTokens} Card Token");
            else if (winner == 0 && !built && !library.HasStamp(cup, profile))
            {
                // Why not (Library.CountsForStamp): a loaner, your edit of a partner's deck (Loaners.PartnerEditOf), or Ban Passes.
                var why = lent ? "a loaner deck" : _campaign.PartnerEditOf(_pickDeck[0]).Length > 0 ? "a partner's edit is theirs to bring" : "Ban Passes used";
                r.Unlocks.Add($"This win doesn't count toward the {c.Name} stamp: {why}. It needs a deck you built, no loaner, no Ban Passes");
            }
            if (alleyOpened) r.Unlocks.Add("A new district: the Back Alley, beside the Library on the map");
            AddGrants(r, reward);
            _scenes.LastResults = r;
            _scenes.ShowResults(r,
                continueAction: () => PlayResultStory(story, () =>
                {
                    // The cup that opened the final hands over to it.
                    if (reward.UnlockedCircuit != null) _campaignScreen.SelectLibrary(library.Cups.Count);
                    _campaignScreen.Open(reward.Summary);
                }),
                rematch: Requeued(story, () => ShowCupVersus(cup, match)),
                shop: Requeued(story, () => _shop.Open()));
        }

        private void RefreshBoard()
        {
            if (_board != null) _view.Render(_board);
                if (_narrator != null) _view.ShowChain(_narrator.Chain.Links);
        }

        /// <summary>The Library era mat you put on your side (its id and look), or null for none (or one not painted).</summary>
        private (string id, Playmat.Signature look)? YourMat() =>
            _campaign.Profile is Profile p && p.Mat is { Length: > 0 } id && p.Mats.Contains(id) && Playmat.Signatures.TryGetValue(id, out var look)
                ? (id, look) : ((string, Playmat.Signature)?)null;

        /// <summary>The tag duel being played (null for one-on-one): the four duelists and their decks.</summary>
        private TagLineup? _tag;
        private TagSetupScreen _tagSetup = null!;
        /// <summary>Tag duels: each team's partner (by team).</summary>
        private Personality[] _partners = Array.Empty<Personality>();
        private TagSeat[] _tagSeats = Array.Empty<TagSeat>();

        /// <summary>A team's name: the duelist, or "lead & partner" in a tag duel.</summary>
        private string TeamName(int team) => _tag != null ? $"{_players[team].Name} & {_partners[team].Name}" : _players[team].Name;

        /// <summary>The campaign Tag Cup match being played: cup, match, and your partner's id.</summary>
        private (int cup, int match, string partner)? _campaignTag;

        private void NewDuel((int circuit, int match)? campaignMatch = null, TagLineup? tag = null, (int cup, int match, string partner)? campaignTag = null,
            int? chainLesson = null, (Guest guest, bool tag, string partner)? guestDuel = null, (int cup, int match)? campaignCup = null, int? campaignRound = null,
            int? bracketMatch = null, bool bracketWatch = false, int? alleyTable = null, LadderSeat? ladderSeat = null)
        {
            // The last duel's results, still waiting their 2 s, aren't this one's: they stay away (DuelController.Results).
            _duelSerial++;
            // A held alley duel left for another one is a loss (DuelController.Alley).
            AbandonAlleyDuel();
            _alleyTable = alleyTable;
            if (alleyTable == null) { _alleyOpp = null; _alleyWho = null; }
            // An exhibition or a combo clip brings its own sides and decks (DuelController.Show); any other duel clears it.
            _showDuel = _showDuelNext;
            _showDuelNext = null;
            // An alley bracket's duels play as the alley's do: its rules, the turn cap, the alley's mat, music and top bar.
            bool alleyBracket = (bracketMatch != null || bracketWatch) && _campaign.Profile?.Bracket is Bracket ab && Brackets.IsAlley(ab);
            if (bracketMatch == null && !bracketWatch) { _bracketSides[0] = _bracketSides[1] = null; _bracketSeats[0] = _bracketSeats[1] = -1; }
            var lesson = chainLesson is int li ? LessonAt(li) : null;
            _chainLesson = lesson;
            _lessonJudge = lesson?.Judge();
            _lessonHint = "";
            _lessonOver = false;
            _lessonMode = ResponseMode.Auto;
            _campaignMatch = campaignMatch;
            _campaignTag = campaignTag;
            _campaignCup = campaignCup;
            _campaignRound = campaignRound;
            _bracketMatch = bracketMatch;
            _bracketWatch = bracketWatch;
            _guestDuel = guestDuel;
            _ladderDuel = ladderSeat;
            // Your campaign duel is live in the save until its result is recorded (Duel! wrote it before the toss; a duel
            // started another way, here). DuelController.LiveDuel.
            if (LiveDuelNow(tag) is LiveDuel live) _campaign.BeginLiveDuel(live);
            _lessonReplay = null;
            _lessonTurn = 0;
            _view.SetLesson(null, null, null);
            _view.ShowChanges(null);
            _view.SetResultButtons(null);
            _tag = tag;
            _watching = null;
            _view.SetReplayMode(false);
            _duel?.Dispose();
            _board = new BoardState();
            // What happened in the duel, for Gus's bounty board (it listens beside the board; replays keep none).
            _tally = _showDuel == null ? new DuelTally(_cardPack) : null; // a show duel pays no bounties
            _seen = null; // (set once the seats are known, below)
            _view.ClearLog();
            _view.Unpin();
            _skipping = false;
            _view.Skipping = false;
            _view.CloseSkipRecap();

            Personality You() => new Personality { Id = "you", Name = "You" };
            int rulesCircuit = campaignMatch?.circuit ?? (campaignTag != null || guestDuel != null || ladderSeat != null ? _loanerCircuit : -1);
            // A Library cup's rules are its own (its list and pool, whatever the campaign's mode); a circuit's follow the mode.
            var rules = alleyTable is int alleyAt ? _campaign.AlleyRulesFor(alleyAt) : ladderSeat != null ? _campaign.LadderRules : campaignCup is { } rc ? _campaign.CupRules(rc.cup) : campaignRound is int rround ? _campaign.RoundRules(rround)
                : (bracketMatch != null || bracketWatch) && BracketRules() is FormatRules bracketRules ? bracketRules : _campaign.Rules(rulesCircuit);
            Deck LoadDeck(string d)
            {
                // An alley opponent's deck, rebuilt from the save as it was met.
                if (Alley.IsAlleyDeck(d)) return _campaign.AlleyDeck(d) ?? new Deck { Name = d };
                // A loaner: the recipe, made legal for the circuit's ban list (a cup's list and pool).
                if (Loaners.IsLoaner(d))
                {
                    var loaner = _campaign.Loaner(d, rules);
                    loaner.Name = $"{DeckTitle(Loaners.RecipeOf(d))} (loaner)";
                    loaner.Notes["loaner"] = Loaners.RecipeOf(d); // it plays by its recipe's playbook
                    return loaner;
                }
                var deck = _library.Load(d);
                deck.Name = d;
                return deck;
            }
            bool humanSeat = lesson != null || (tag != null ? tag.YouPlay : _pickCharacter[0] < 0);
            Deck[] decks, partnerDecks = Array.Empty<Deck>();
            if (tag != null)
            {
                // Slots: 0 your lead, 1 your partner, 2 their lead, 3 their partner.
                _players = new[] { tag.YouPlay ? You() : _roster[tag.Characters[0]], _roster[tag.Characters[2]] };
                _partners = new[] { tag.PartnerStyle ?? _roster[tag.Characters[1]], _roster[tag.Characters[3]] };
                decks = new[] { LoadDeck(tag.Decks[0]), LoadDeck(tag.Decks[2]) };
                // Your partner answers to the list as you do (era-limited play); the other team plays its decks as built.
                var partnerDeck = LoadDeck(tag.Decks[1]);
                if (!Loaners.IsLoaner(tag.Decks[1])) { partnerDeck = _campaign.PartnerPlays(tag.Decks[1], rulesCircuit); partnerDeck.Name = tag.Decks[1]; }
                partnerDecks = new[] { partnerDeck, LoadDeck(tag.Decks[3]) };
            }
            else if (lesson != null)
            {
                _players = new[] { You(), _roster.FirstOrDefault(p => p.Id == "old-gus") ?? new Personality { Id = "old-gus", Name = "Gus" } };
                _partners = Array.Empty<Personality>();
                decks = lesson.Decks.Select(d => { var c = d.Clone(); c.Name = "Lesson"; return c; }).ToArray();
            }
            else if (_showDuel is { } showDuel)
            {
                _players = showDuel.Who.ToArray();
                _partners = Array.Empty<Personality>();
                decks = showDuel.Decks.Select(d => { var c = d.Clone(); c.Name = d.Name; return c; }).ToArray();
            }
            else
            {
                // An alley bracket's sides as the bracket has them (a splash regular as its handle); else the roster's.
                _players = new[]
                {
                    humanSeat ? You() : alleyBracket && _bracketSides[0] != null ? _bracketSides[0]! : _roster[_pickCharacter[0]],
                    // A ladder seat's holder plays at skill 1.0 whatever their profile says (Ladder.Opponent).
                    ladderSeat != null ? Ladder.Opponent(ladderSeat)
                    : alleyTable != null && _alleyWho != null ? _alleyWho : alleyBracket && _bracketSides[1] != null ? _bracketSides[1]! : _roster[_pickCharacter[1]],
                };
                _partners = Array.Empty<Personality>();
                decks = _pickDeck.Select(LoadDeck).ToArray();
            }
            _announcedResult = false;

            // The music: a boss's theme (BossMusic) or the everyday one. The tune comes in with their ace: a boss monster
            // in their playbook, or any Level 8+ or 3000+ ATK monster.
            var (theme, key) = campaignMatch is { } cm ? BossMusic.ForMatch(_campaign.Circuits, cm.circuit, cm.match)
                : campaignTag is { } ct ? BossMusic.ForTagCup(_campaign.Circuits, ct.cup, ct.match)
                : guestDuel is { } gd ? BossMusic.ForGuest(gd.guest)
                : ladderSeat != null ? LadderMusic(_campaign.Ladder, ladderSeat)
                : campaignCup is { } lc ? CupMusic(lc.cup, lc.match)
                : campaignRound is int mround ? RoundMusic(mround)
                : bracketMatch != null || bracketWatch ? BracketMusic(bracketMatch)
                : alleyTable is int mtable ? BossMusic.ForAlley(_campaign.AlleyData, mtable, _alleyOpp?.Seat ?? 1)
                : _showDuel != null ? (BossMusic.Alley, 0) : (BossMusic.Everyday, 0);
            Sounds.DuelTheme(theme, key);
            // The alley's everyday music has no boss's tune to hold: it follows anyone's ace, as the everyday theme does.
            _bossMusic = theme != BossMusic.Everyday && theme != BossMusic.Alley;
            _bossAceSeen = false;
            _bossPlaybooks = decks.Skip(1).Concat(partnerDecks.Skip(1)).Select(d => Playbooks.ForDeck(d)).OfType<Playbook>().ToArray();

            // Your deck plays the texts this table allows (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.2): an original past its
            // errata plays its current text, as the versus screen said. Your seat in a campaign duel only: CPU decks (your
            // partner's too), lessons, shows and free duels play as written. The replay records the codes played.
            // Phase 2: a slot asking for an original needs a copy carrying it (Collection.Assign, per text, on the deck as
            // built), and a slot without one plays its current text (spec 4.4, D3: a note, not a refusal).
            _textsDowngraded = new List<uint>();
            bool campaignDuel = campaignMatch != null || campaignTag != null || campaignCup != null || campaignRound != null
                                || guestDuel != null || bracketMatch != null || alleyTable != null || ladderSeat != null;
            var owner = humanSeat && lesson == null ? _campaign.Profile : null;
            // Your copies: each of your deck's slots plays one (best first), and the cards carry their slot through the duel.
            var yourCopies = owner?.Collection.Assign(decks[0], _cardPack!);
            if (humanSeat && lesson == null && _showDuel == null && campaignDuel)
                decks[0] = PlayTexts.ForTable(decks[0], yourCopies, rules.Texts, _campaign.Texts, _cardPack!, out _textsDowngraded);
            // Your deck in your partner's hands (lent to Kit, or your edit of theirs) plays the same texts, from your copies;
            // their own decks and loaners play as authored.
            if (tag != null && lesson == null && _showDuel == null && campaignDuel && _campaign.PlaysYourTexts(tag.Decks[1]))
            {
                var partnerCopies = _campaign.Profile?.Collection.Assign(partnerDecks[0], _cardPack!);
                partnerDecks[0] = PlayTexts.ForTable(partnerDecks[0], partnerCopies, rules.Texts, _campaign.Texts, _cardPack!, out var partnerDown);
                _textsDowngraded.AddRange(partnerDown.Where(c => !_textsDowngraded.Contains(c)));
            }
            decks[0].Looks = yourCopies?.Select(c => c?.Look ?? default).ToArray();
            // The recipe recreates this exact duel; skilled characters replay it to plan ahead.
            // Who starts: the coin toss's result (you, the left side, unless the toss said otherwise), used once.
            var settings = new DuelSettings { Seed = (ulong)seed, FirstPlayer = _nextFirstPlayer };
            _nextFirstPlayer = _checkFirstPlayer;
            var recipe = lesson != null ? lesson.Recipe(_cardPack!, _scripts) : tag != null
                ? TagDuel.Recipe(_cardPack!, _scripts, settings, decks[0], partnerDecks[0], decks[1], partnerDecks[1])
                : new DuelRecipe { Cards = _cardPack!, Scripts = _scripts, Settings = settings, Deck0 = decks[0], Deck1 = decks[1] };
            // Checks only: a board set up before the duel starts (the pile check).
            if (_checkSetup != null && lesson == null && tag == null) { recipe.Setup.AddRange(_checkSetup); _checkSetup = null; }
            var answers = new ResponseLog();
            _recording = new Replay
            {
                Settings = recipe.Settings,
                Decks = new[] { decks[0].Clone(), decks[1].Clone() },
                Names = _players.Select(p => p.Name).ToArray(),
                Characters = _players.Select(p => p.Id).ToArray(),
                DeckNames = decks.Select(d => d.Name).ToArray(),
                HumanSeat = humanSeat ? 0 : -1,
                CardCount = _cardPack!.Count,
                Responses = answers.Responses, // fills in as the duel is played
            };
            if (tag != null)
            {
                _recording.Partners = partnerDecks.Select(d => d.Clone()).ToArray();
                _recording.PartnerNames = _partners.Select(p => p.Name).ToArray();
                _recording.PartnerCharacters = _partners.Select(p => p.Id).ToArray();
                _recording.PartnerDeckNames = partnerDecks.Select(d => d.Name).ToArray();
            }
            // The replay keeps how your copies looked in this duel.
            _recording.Looks[0] = decks[0].Looks;
            // Lessons start from a set board, so a replay of one couldn't be rebuilt from its decks.
            if (lesson != null) _recording = null;
            // An exhibition or a combo clip is only a show (a clip is the goldfish game again): not one of your recent duels.
            if (_showDuel != null) _recording = null;
            _ticks = 0;
            _duel = recipe.Create();
            CardFoil.DuelLooks = _duel.Looks;
            _view.ResetPrizes();
            _duel.Log += (text, type) => { if (type == 0) _view.AppendLog($"[script error] {text}"); };
            // The board learns LP from changes, so it starts where the duel does (a lesson's 2000, not 8000).
            foreach (var player in _board.Players) player.LifePoints = (int)recipe.Settings.StartingLP;

            var names = _players.Select(p => p.Name).ToArray();
            _view.SetPlayers(tag != null ? new[] { TeamName(0), TeamName(1) } : names, decks.Select((d, i) => PlateTitle(d, decks[1 - i])).ToArray());
            // Tag duels open with each team's core duelist 0 in play: your lead, and their partner (their lead swaps in for
            // their first turn).
            var narrator = new DuelNarrator(_cardPack!, names[0], tag != null ? _partners[1].Name : names[1]) { HiddenPlayer = humanSeat ? 1 : -1 };
            var namedDuel = _duel;
            narrator.CodeAt = place => BoardState.PublicCodeAt(namedDuel, place);
            _narrator = narrator;
            if (tag != null)
            {
                _view.SetPlayerName(0, TeamName(0), DeckTitle(decks[0].Name));
                _view.SetPlayerName(1, $"{_partners[1].Name} & {_players[1].Name}", DeckTitle(partnerDecks[1].Name));
            }
            narrator.Line += _view.AppendLog;
            // The history strip and drawer, and the messages for what you'd miss (told from your seat).
            var history = new DuelHistory(_cardPack!, names[0], tag != null ? _partners[1].Name : names[1])
            {
                HiddenPlayer = humanSeat ? 1 : -1, Viewer = humanSeat ? 0 : -1, CodeAt = narrator.CodeAt,
            };
            history.Notable += (kind, code, text) => _view.Toast(kind, code, text);
            _history = history;
            _view.SetHistory(history);
            if (tag != null) history.SetName(0, TeamName(0));
            var context = new DuelContext(_duel, _cardPack!);

            // Bottom seat: you or a character. In human play the opponent's hand and set cards stay hidden.
            _human = humanSeat ? new HumanDuelist(context, seed) : null;
            // You choose where your cards go (click a glowing zone).
            if (_human != null)
            {
                _human.Presenter.AskForZones = true;
                _human.Presenter.Mode = lesson != null ? _lessonMode : ResponseSetting;
                _human.Presenter.HoldToAsk = HoldingAsk;
            }
            _view.SetResponseMode(_human != null, lesson != null ? _lessonMode : ResponseSetting);
            if (_human != null)
            {
                var human = _human;
                human.Changed += () =>
                {
                    if (human.Current != null) { _promptShownAt = Time.realtimeSinceStartup; _promptShownTitle = human.Current.Title; }
                    _view.ShowChoices(human.Current, human.PreviousAnswerRejected);
                };
            }
            _answerReady = false;
            _view.HiddenSeat = humanSeat ? 1 : -1;
            _view.SetHumanMode(humanSeat);
            // The campaign layout for campaign duels: circuit (or Tag Cup) and match at the top, no test tools.
            string campaignTitle = "";
            if (campaignMatch is { } cmatch)
            {
                var circuitInfo = _campaign.Circuits.All[cmatch.circuit];
                campaignTitle = $"{circuitInfo.Name}  <color=#B8C0D0>·  Match {cmatch.match + 1} of {circuitInfo.Matches.Count}</color>";
            }
            else if (campaignTag is { } ctag)
            {
                var cupInfo = _campaign.Circuits.TagCups[ctag.cup];
                campaignTitle = $"{cupInfo.Name}  <color=#B8C0D0>·  Match {ctag.match + 1} of {cupInfo.Matches.Count}</color>";
            }
            else if (campaignCup is { } lcup)
            {
                var libCup = _campaign.Library.Cups[lcup.cup];
                campaignTitle = $"{libCup.Name}  <color=#B8C0D0>·  {(libCup.Matches[lcup.match].Champion ? "Champion's table" : $"Match {lcup.match + 1} of {libCup.Matches.Count}")}</color>";
            }
            else if (campaignRound is int tround)
            {
                var lib = _campaign.Library;
                campaignTitle = $"{lib.Final.Name}  <color=#B8C0D0>·  Round {tround + 1} of {lib.Rounds.Count}, {lib.Cups[lib.Rounds[tround].Cup].Era}</color>";
            }
            else if ((bracketMatch != null || bracketWatch) && _campaign.Profile?.Bracket is Bracket titleBracket)
                campaignTitle = $"{titleBracket.Name}  <color=#B8C0D0>·  {BracketStage(titleBracket, bracketMatch, bracketWatch)}</color>";
            else if (alleyTable is int atable && _campaign.AlleyData.Tables.Count > atable)
                campaignTitle = $"{_campaign.AlleyData.Tables[atable].Name}  <color=#B8C0D0>·  {(_alleyOpp?.Seat is int aseat && aseat > 0 ? $"Seat {aseat} of {_campaign.AlleyData.Tables[atable].SeatCount}" : "Encore")}</color>";
            else if (_showDuel != null)
                campaignTitle = _showDuel.Title;
            else if (ladderSeat != null)
                campaignTitle = $"The Champion's Ladder  <color=#B8C0D0>·  {(ladderSeat.IsVisitor ? $"{ladderSeat.Character.Name}, visiting" : $"Rung {ladderSeat.Rung}, {ladderSeat.Character.Name}")}</color>";
            else if (guestDuel is { } gduel)
                campaignTitle = $"Special guest  <color=#B8C0D0>·  {gduel.guest.Character.Name}{(gduel.tag ? " & " + gduel.guest.Partner.Name : "")}</color>";
            if (lesson != null) campaignTitle = lesson == ChainLessons.FirstDuel ? "Gus's Lessons  <color=#B8C0D0>·  First duel</color>"
                : $"Gus's Lessons  <color=#B8C0D0>·  {chainLesson + 1} of {ChainLessons.All.Count}</color>";
            else if (campaignTitle.Length == 0) campaignTitle = tag != null ? "Tag Duel" : humanSeat ? "Free Duel" : "Exhibition";
            // An alley duel's top bar reads the alley's theme (and only there).
            _view.AlleyBar = alleyTable != null || alleyBracket || _showDuel != null;
            _view.SetDuelTitle(campaignTitle);
            // Each side's mat: a picture (mats/<character or deck>.png), else their signature mat, else painted from the
            // deck's Attribute. Your side shows the Library era mat you put on, if any.
            for (int seat = 0; seat < 2; seat++)
            {
                if (seat == 0 && humanSeat && lesson == null && YourMat() is { } yours)
                {
                    _view.Mat.SetTheme(0, yours.look.Theme, Playmat.LoadCustom(yours.id), yours.look.Motif);
                    if (_recording != null) _recording.Mats[0] = yours.id; // the replay shows it too
                    continue;
                }
                var matDeck = tag != null && seat == 1 ? partnerDecks[1] : decks[seat];
                var who = tag != null && seat == 1 ? _partners[1] : _players[seat];
                var picture = Playmat.LoadCustom(who.Id, matDeck.Name);
                if (Playmat.Signatures.TryGetValue(who.Id, out var signature))
                    _view.Mat.SetTheme(seat, signature.Theme, picture, signature.Motif);
                // An alley regular sits at its table's mat (campaign/mats.txt, when it has one); a bracket's at the table it came from.
                else if ((alleyTable is int matTable && seat == 1 ? _campaign.AlleyData.Tables[matTable].Mat : alleyBracket ? BracketMat(seat) : null) is string matKey
                         && Playmat.Signatures.TryGetValue(matKey, out var tableMat))
                {
                    _view.Mat.SetTheme(seat, tableMat.Theme, picture, tableMat.Motif);
                    if (_recording != null) _recording.Mats[seat] = matKey;
                }
                else if (picture != null) _view.Mat.SetTheme(seat, Playmat.ThemeFor(matDeck, _cardPack!), picture);
                else _view.Mat.SetTheme(seat, Playmat.Space(seat), null, "space"); // the default: the space look
            }
            // Portraits by the LP: in a tag duel, whoever is in play (your lead, and their partner at first).
            _view.SetPortrait(0, _players[0].Id);
            _view.SetPortrait(1, tag != null ? _partners[1].Id : _players[1].Id);
            // A nameless alley regular (table or bracket) shows its grunt's face, picked by its handle.
            if (tag == null && _players[1].Id == BackAlley.RegularId && YgoCampaign.Game.Portraits.GruntFor(_players[1].Name) is string grunt)
                _view.SetPortrait(1, grunt);
            // Tag duels: whoever waits sits in the panel's round chip (your partner; their lead, as their partner starts).
            _view.SetPartner(0, tag != null ? _partners[0].Id : null, tag != null ? _partners[0].Name : "");
            _view.SetPartner(1, tag != null ? _players[1].Id : null, tag != null ? _players[1].Name : "");
            _reactions.Reset((int)recipe.Settings.StartingLP, (int)recipe.Settings.StartingLP);
            // The headliners' show: which seats are headliners', their routes watched as their bots are made (DuelController.Show).
            ResetShow();
            // The deck a bot plays by: a loaner, a custom loaner or a deck built from a recipe by its recipe (its playbook).
            static string PlaysAs(Deck d) => Playbooks.RecipeOf(d);
            IDuelist Character(Personality style, string deckName, int salt)
            {
                var book = style.PlansAhead || style.KnowsDeck ? Playbooks.ForDeck(deckName) : null;
                WatchRoutes(salt, book, context); // a headliner's route signals, for the show
                if (!style.PlansAhead)
                    return new SmartDuelist(style.Name, seed * 4 + salt, context, personality: style, playbook: book);
                return new SmartDuelist(style.Name, seed * 4 + salt, context, personality: style,
                    playbook: book,
                    planner: new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(deckName)));
            }
            IDuelist Bot(int seat) => _showDuel?.Bot?.Invoke(seat, context)
                                      ?? (lesson != null && seat == 1 ? lesson.Gus(_cardPack!) : Character(_players[seat], PlaysAs(decks[seat]), seat));
            IDuelist seat0 = _human ?? Bot(0);
            // QA play: a skilled stand-in decides your moves (they're still given through the prompt panel).
            _qaAdvisor = null;
            if (_qaPlaying && _human != null)
            {
                var style = QaStyle();
                var yourDeck = PlaysAs(decks[0]);
                // It looks ahead in tag duels too, as the tag CPUs and the wall study's stand-in do (the planner replays the
                // tag recipe); without it the stand-in won 52% where the study measured 70% (Expert Tag Cup 1).
                _qaAdvisor = new SmartDuelist("You", seed * 4 + 5, context, personality: style, playbook: Playbooks.ForDeck(yourDeck),
                    planner: new TurnPlanner(recipe, answers, style, () => Playbooks.ForDeck(yourDeck)));
            }
            if (tag != null)
            {
                // Each team's prompts go to whichever of its two duelists is in play.
                var (a, b) = TagDuel.Seats(seat0, Character(_partners[0], PlaysAs(partnerDecks[0]), 2), Bot(1), Character(_partners[1], PlaysAs(partnerDecks[1]), 3),
                    settings.FirstPlayer);
                _tagSeats = new[] { a, b };
                _runner = new DuelRunner(_duel, a, b, answers);
                _runner.MessageReceived += a.Observe;
                _runner.MessageReceived += b.Observe;
                bool firstTurn = true;
                _runner.MessageReceived += message =>
                {
                    // The core announces a turn, then swaps the team's duelist: name the one about to play in the
                    // turn's heading (this runs before the narrator).
                    if (message[0] == Msg.NewTurn)
                    {
                        if (firstTurn) { firstTurn = false; return; }
                        int turnTeam = message[1];
                        bool next = TagDuel.Role(_tagSeats[turnTeam]) == 0;
                        narrator.SetName(turnTeam, (next ? _partners[turnTeam] : _players[turnTeam]).Name);
                        history.SetName(turnTeam, (next ? _partners[turnTeam] : _players[turnTeam]).Name);
                        return;
                    }
                    if (message[0] != Msg.TagSwap) return;
                    int team = message[1];
                    bool partner = TagDuel.Role(_tagSeats[team]) == 1;
                    var who = partner ? _partners[team] : _players[team];
                    var other = partner ? _players[team] : _partners[team];
                    _view.SetPlayerName(team, $"{who.Name} & {other.Name}", DeckTitle(partner ? partnerDecks[team].Name : decks[team].Name));
                    narrator.SetName(team, who.Name);
                    history.SetName(team, who.Name);
                    _view.SetPortrait(team, who.Id);
                    _view.SetPartner(team, other.Id, other.Name);
                    _view.AppendLog(team == 0 && humanSeat
                        ? (partner ? $"{who.Name} takes over: your partner plays this turn." : "Your turn again.")
                        : $"{who.Name} steps in for {(team == 0 ? "your" : "their")} team.");
                };
            }
            else
            {
                _tagSeats = Array.Empty<TagSeat>();
                _runner = new DuelRunner(_duel, seat0, Bot(1), answers);
            }
            _runner.MessageReceived += context.Observe;
            // Who the engine gives turn 1 (the coin toss's result as played; checks and QA compare it).
            FirstTurnPlayer = -1;
            _runner.MessageReceived += m => { if (m[0] == Msg.NewTurn && FirstTurnPlayer < 0) FirstTurnPlayer = m[1]; };
            if (_qaAdvisor is { } advisor)
                _runner.MessageReceived += m => { if (m[0] == Msg.NewTurn || m[0] == Msg.NewPhase) advisor.OnPhaseChanged(); };
            _runner.MessageReceived += narrator.Handle;
            _runner.MessageReceived += history.Handle;
            YataLock.Attach(_runner, _duel);
            // The alley's turn cap: turn 41 never starts; the higher LP wins (only in alley duels, its brackets' too).
            _turnCap = alleyTable != null || alleyBracket || _showDuel?.Kind == "exhibition" ? TurnCap.Attach(_runner, _duel, _alleyTurnCapOverride) : null;
            _view.TurnCapLast = _turnCap?.LastTurn ?? 0;
            if (_lessonJudge != null) _runner.MessageReceived += _lessonJudge.Handle;
            WatchMessages();
            if (_tally != null) _runner.MessageReceived += _tally.Observe;
            // The opponent's cards you saw, for the editor's Seen in duels: your own duels only (seat 0, your team in a tag
            // duel), never a show duel, a lesson, a duel you watch, or a check run that doesn't ask (the search check does).
            _seen = _showDuel == null && lesson == null && humanSeat && !bracketWatch && (!IsCheckRun || DeckEditor.SeenInChecks)
                ? new SeenCards(_cardPack, 0) : null;
            if (_seen != null) _runner.MessageReceived += _seen.Observe;

            _view.AppendLog(tag != null
                ? $"Tag Duel {seed}: {TeamName(0)} ({decks[0].Name} + {partnerDecks[0].Name}) vs {TeamName(1)} ({decks[1].Name} + {partnerDecks[1].Name})"
                : $"Duel {seed}: {_players[0].DisplayName} ({decks[0].Name}) vs {_players[1].DisplayName} ({decks[1].Name})");
            foreach (var p in _players.Concat(_partners))
                if (p.Intro.Length > 0) _view.AppendLog($"{p.Name}: \"{p.Intro}\"");
            var starter = settings.FirstPlayer == 0 ? TeamName(0) : TeamName(1);
            _view.AppendLog($"{starter} {(starter == "You" ? "go" : "goes")} first.");
            Debug.Log($"duel {seed}: {TeamName(0)} vs {TeamName(1)}, {starter} first");
            _view.Fx.ArmOpeningShuffle(); // both Decks riffle before the opening hands fly out (BoardFx.OpeningShuffle)
            _view.Render(_board);
                if (_narrator != null) _view.ShowChain(_narrator.Chain.Links);
            UpdateControls();
        }

        // ---- Gus's chain lessons ----------------------------------------------------------------------

        private ChainLesson? _chainLesson;
        /// <summary>For the pile check: cards to place before the next duel starts.</summary>
        private List<PlacedCard>? _checkSetup;
        private LessonJudge? _lessonJudge;
        private string _lessonHint = "";
        private bool _lessonOver;
        /// <summary>The Chain pill during a lesson: starts on Auto, and changing it doesn't touch your saved setting.</summary>
        private ResponseMode _lessonMode;

        /// <summary>A lesson by its index in <see cref="ChainLessons.All"/>; -1 is Gus's guided first duel.</summary>
        private static ChainLesson LessonAt(int index) => index < 0 ? ChainLessons.FirstDuel : ChainLessons.All[index];

        private void ShowLessons()
        {
            _paused = true;
            _chainLesson = null;
            _view.SetLesson(null, null, null);
            var profile = _campaign.Profile;
            // The first duel heads the list, at index -1.
            var list = new[] { ChainLessons.FirstDuel }.Concat(ChainLessons.All).ToList();
            _scenes.ShowLessons(list, l => profile?.Seen.Contains(l.SeenKey) == true, i => BeginChainLesson(i - 1), () => _campaignScreen.Open());
        }

        /// <summary>
        /// A new campaign, after the opening scene: Gus offers his guided first duel (once; it stays under Lessons), or
        /// straight on to the circuits.
        /// </summary>
        private void OfferFirstDuel()
        {
            var profile = _campaign.Profile;
            if (profile == null || profile.Seen.Contains(ChainLessons.FirstDuel.SeenKey) || !profile.Seen.Add("offer:first-duel"))
            {
                _campaignScreen.Open();
                return;
            }
            _campaign.Save();
            _paused = true;
            _scenes.ShowFirstDuelOffer(practice: () => BeginChainLesson(-1), skip: () => { _scenes.Close(); _campaignScreen.Open(); });
        }

        /// <summary>Gus explains the lesson, then it starts.</summary>
        private void BeginChainLesson(int index)
        {
            var lesson = LessonAt(index);
            var scene = new StoryScene { Id = "lesson-intro:" + lesson.Id };
            foreach (var line in lesson.Intro) scene.Lines.Add(new StoryLine("old-gus", line));
            if (_campaign.Profile == null) { StartChainLesson(index); return; }
            _storyScreen.Play(new[] { scene }, _campaign.Profile, null, () => StartChainLesson(index));
        }

        private void StartChainLesson(int index)
        {
            _paused = false;
            NewDuel(chainLesson: index);
            UpdateControls();
        }

        /// <summary>Keeps the lesson's hint current and, once it's passed or failed, shows Gus's verdict (called every frame).</summary>
        private void WatchChainLesson()
        {
            if (_chainLesson == null || _lessonJudge == null || _lessonOver || _watching != null) return;
            if (_lessonJudge.Hint != _lessonHint)
            {
                _lessonHint = _lessonJudge.Hint;
                _view.SetLesson(_chainLesson.Title, _lessonHint, ShowLessons);
            }
            _view.PointAt(_lessonJudge.Pointer);
            if (_lessonJudge.Outcome == LessonOutcome.Playing) return;
            _lessonOver = true;
            _paused = true;
            StartCoroutine(FinishChainLesson(_chainLesson, _lessonJudge));
        }

        private System.Collections.IEnumerator FinishChainLesson(ChainLesson lesson, LessonJudge judge)
        {
            bool passed = judge.Outcome == LessonOutcome.Passed;
            if (passed && _campaign.Profile != null)
            {
                _campaign.Profile.Seen.Add(lesson.SeenKey);
                _campaign.Save();
            }
            Sounds.Play(passed ? "win" : "turn");
            // A moment to see the board as it ended.
            yield return new WaitForSecondsRealtime(2.2f);
            if (_chainLesson != lesson) yield break;
            int index = ChainLessons.All.ToList().IndexOf(lesson);
            // The first duel leads on to the circuits (or skips there), not to the chain lessons: those come after the
            // Beginner Circuit.
            if (lesson == ChainLessons.FirstDuel)
                _scenes.ShowLessonResult(lesson, passed, judge.Hint,
                    again: () => BeginChainLesson(-1),
                    next: () => { _scenes.Close(); _chainLesson = null; _campaignScreen.Open(); },
                    back: ShowLessons, nextLabel: passed ? "On to the circuit" : "Skip for now");
            else
                _scenes.ShowLessonResult(lesson, passed, judge.Hint,
                    again: () => BeginChainLesson(index),
                    next: passed && index + 1 < ChainLessons.All.Count ? () => BeginChainLesson(index + 1) : null,
                    back: ShowLessons);
        }

        /// <summary>
        /// For checks: plays the lesson in progress as a student would through the prompts (the named cards when offered,
        /// else end the turn or pass), with a screenshot the first time the board shows a chain or the hint, until Gus's
        /// verdict is showing.
        /// </summary>
        private System.Collections.IEnumerator PlayChainLesson(uint[] activate, uint[] chain, uint[] targets, string shot, Func<string, System.Collections.IEnumerator> takeShot)
        {
            var used = new HashSet<uint>();
            bool shotTaken = false;
            var deadline = Time.realtimeSinceStartup + 90;
            while (Time.realtimeSinceStartup < deadline && !(_lessonOver && _scenes.IsOpen))
            {
                yield return null;
                if (!shotTaken && _chainLesson != null && (_narrator?.Chain.Links.Count >= 2 || (shot == "lesson-banner" && _board.Turn == 2)))
                {
                    shotTaken = true;
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return takeShot(shot);
                }
                var set = _human?.Current;
                if (set == null || _runner == null || !_runner.IsWaiting || _answerReady || _lessonOver) continue;
                int Find(Func<Choice, bool> want) => set.Choices.FindIndex(c => want(c));
                byte[] answer;
                if (set.IsChain)
                {
                    int i = Find(c => c.Card is { } card && chain.Contains(card.Code) && !used.Contains(card.Code));
                    if (i >= 0) used.Add(set.Choices[i].Card!.Value.Code);
                    answer = i >= 0 ? set.Choose(i) : set.CanCancel ? set.Cancel() : set.Choose(0);
                }
                else if (set.Prompt is Ygo.Engine.Protocol.IdlePrompt)
                {
                    int i = Find(c => c.Card is { } card && activate.Contains(card.Code) && !used.Contains(card.Code) && c.Label.StartsWith("Activate"));
                    if (i >= 0) used.Add(set.Choices[i].Card!.Value.Code);
                    answer = set.Choose(i >= 0 ? i : Math.Max(0, Find(c => c.Label == "End your turn")));
                }
                else
                {
                    int target = Find(c => c.Card is { } card && targets.Contains(card.Code));
                    answer = set.Mode == ChoiceMode.Multi ? set.Choose(Enumerable.Range(0, set.Min).Select(n => n == 0 && target >= 0 ? target : n).Distinct().ToList())
                        : set.Mode == ChoiceMode.Incremental && set.CanFinish ? set.Finish()
                        : set.Choose(Math.Max(0, target));
                }
                _human!.Submit(answer);
                _answerReady = true;
            }
            yield return new WaitForSecondsRealtime(0.4f);
        }

        /// <summary>
        /// For checks: plays Gus's first duel as a new player following the hints (summon, set, end; Sakuretsu Armor on his
        /// attack; then Axe of Despair, the Battle Phase and a direct attack), with a screenshot the first time each hint's
        /// pointer is up (checking the board points where the hint says), until Gus's verdict is showing.
        /// </summary>
        private System.Collections.IEnumerator PlayFirstDuel(Func<string, System.Collections.IEnumerator> takeShot, Action<string, bool> report)
        {
            var plan = new[] { "Normal Summon Celtic Guardian", "Set Sakuretsu Armor", "3:Activate Axe of Despair", "Attack with Celtic Guardian" };
            var shots = new HashSet<string>();
            int turn = 0;
            string waitingFor = "";
            float waitingSince = 0;
            var deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline && !(_lessonOver && _scenes.IsOpen))
            {
                yield return null;
                if (_lessonJudge == null) continue;
                turn = _board.Turn;
                // A shot of each pointer target (and of Gus's attack being asked about), once the board has settled.
                var pointer = _lessonJudge.Pointer;
                var key = _lessonJudge.Hint.StartsWith("Gus attacks!") ? "answer" : pointer.Split(':')[0];
                if (key.Length > 0 && !shots.Contains(key) && !_view.Fx.Busy && (key == "answer" ? _view.Prompt.Current != null : key == "lp" || _runner?.IsWaiting == true))
                {
                    shots.Add(key);
                    yield return new WaitForSecondsRealtime(0.3f);
                    if (key != "answer") report($"the board points where the hint says ({pointer}: {_view.PointingAt})", _view.PointingAt == pointer);
                    yield return takeShot("first-duel-" + key);
                }
                // Each new step's shot first (for up to 5 seconds), then the answer.
                if (key.Length > 0 && !shots.Contains(key))
                {
                    if (waitingFor != key) { waitingFor = key; waitingSince = Time.realtimeSinceStartup; }
                    if (Time.realtimeSinceStartup - waitingSince < 5) continue;
                }
                var set = _human?.Current;
                if (set == null || _runner == null || !_runner.IsWaiting || _answerReady || _lessonOver) continue;
                int Find(Func<Choice, bool> want) => set.Choices.FindIndex(c => want(c));
                byte[] answer;
                if (set.IsChain)
                {
                    int i = Find(c => c.Card is { } card && card.Code == ChainLessons.SakuretsuArmor);
                    answer = i >= 0 ? set.Choose(i) : set.CanCancel ? set.Cancel() : set.Choose(0);
                }
                else if (set.Prompt is Ygo.Engine.Protocol.IdlePrompt or Ygo.Engine.Protocol.BattlePrompt)
                {
                    int i = -1;
                    foreach (var entry in plan)
                    {
                        var want = entry.Length > 2 && entry[1] == ':' ? entry.Substring(2) : entry;
                        if (want != entry && turn < entry[0] - '0') continue;
                        if ((i = Find(c => c.Label.StartsWith(want))) >= 0) break;
                    }
                    if (i < 0) i = Find(c => c.Label == "Go to the Battle Phase");
                    answer = set.Choose(i >= 0 ? i : Math.Max(0, Find(c => c.Label == "End your turn")));
                }
                else
                {
                    int target = Find(c => c.Card is { } card && card.Code == ChainLessons.CelticGuardian);
                    answer = set.Mode == ChoiceMode.Multi ? set.Choose(Enumerable.Range(0, set.Min).Select(n => n == 0 && target >= 0 ? target : n).Distinct().ToList())
                        : set.Mode == ChoiceMode.Incremental && set.CanFinish ? set.Finish()
                        : set.Choose(Math.Max(0, target));
                }
                _human!.Submit(answer);
                _answerReady = true;
            }
            yield return new WaitForSecondsRealtime(0.4f);
        }

        /// <summary>The board, log, and animations listen to every core message.</summary>
        private void WatchMessages()
        {
            _runner!.MessageReceived += _board.Observe;
            _runner.MessageReceived += message =>
            {
                _sawVisibleEvent |= IsVisible(message[0]);
                _sawNewTurn |= message[0] == Msg.NewTurn;
                var e = DuelEvent.Parse(message);
                if (e != null) _events.Add(e);
            };
            _events.Clear();
        }

        /// <summary>Plays a recorded duel, optionally jumping straight to a position (in redraws).</summary>
        private void WatchReplay(Replay replay, int skipTo)
        {
            _duelSerial++; // the last duel's results, still waiting their 2 s, aren't this one's (DuelController.Results)
            AbandonAlleyDuel();
            _alleyTable = null;
            _showDuel = null;
            StopShow(); // the last duel's show (its hold, its route, its ending) stays with it
            _view.ShowChanges(null);
            _watching = replay;
            _recording = null;
            _chainLesson = null;
            _duel?.Dispose();
            _board = new BoardState();
            _tally = null; // a replay pays no bounties
            _seen = null; // and adds nothing to Seen in duels
            _view.ClearLog();
            _view.Unpin();
            _skipping = false;
            _view.Skipping = false;
            _view.CloseSkipRecap();
            _announcedResult = false;
            _shownReplayEnd = false;
            _ticks = 0;

            // Characters still on the roster bring their win and lose lines.
            _players = Enumerable.Range(0, 2).Select(seat =>
                _roster.FirstOrDefault(p => p.Id == replay.Characters[seat] && p.Name == replay.Names[seat])
                ?? new Personality { Id = replay.Characters[seat], Name = replay.Names[seat] }).ToArray();
            _duel = replay.Recipe(_cardPack!, _scripts).Create();
            CardFoil.DuelLooks = _duel.Looks;
            _view.ResetPrizes();
            _duel.Log += (text, type) => { if (type == 0) _view.AppendLog($"[script error] {text}"); };
            var teamNames = replay.IsTag ? new[] { $"{replay.Names[0]} & {replay.PartnerNames![0]}", $"{replay.Names[1]} & {replay.PartnerNames[1]}" } : replay.Names;
            _view.SetPlayers(teamNames, replay.DeckNames.Select(DeckTitle).ToArray());
            _view.SetPortrait(0, replay.Characters[0]);
            _view.SetPortrait(1, replay.IsTag ? replay.PartnerCharacters![1] : replay.Characters[1]);
            if (!replay.IsTag && replay.Characters[1] == BackAlley.RegularId && YgoCampaign.Game.Portraits.GruntFor(replay.Names[1]) is string replayGrunt)
                _view.SetPortrait(1, replayGrunt);   // an alley regular's grunt face, as in the duel
            _view.SetPartner(0, replay.IsTag ? replay.PartnerCharacters![0] : null, replay.IsTag ? replay.PartnerNames![0] : "");
            _view.SetPartner(1, replay.IsTag ? replay.Characters[1] : null, replay.IsTag ? replay.Names[1] : "");
            for (int seat = 0; seat < 2; seat++)
            {
                var matDeck = replay.IsTag && seat == 1 ? replay.Partners![1] : replay.Decks[seat];
                var who = replay.IsTag && seat == 1 ? replay.PartnerCharacters![1] : replay.Characters[seat];
                var picture = Playmat.LoadCustom(who, replay.DeckNames[seat]);
                // A mat the duel chose by name (recorded since replay format 5): the era mat you had on (its own picture,
                // if any), or a table's mat (under the seat's picture, as it was dealt).
                if (replay.Mats[seat] is { Length: > 0 } recorded && Playmat.Signatures.TryGetValue(recorded, out var recordedMat))
                    _view.Mat.SetTheme(seat, recordedMat.Theme, seat == replay.HumanSeat ? Playmat.LoadCustom(recorded) : picture, recordedMat.Motif);
                else if (Playmat.Signatures.TryGetValue(who, out var signature))
                    _view.Mat.SetTheme(seat, signature.Theme, picture, signature.Motif);
                else if (picture != null) _view.Mat.SetTheme(seat, Playmat.ThemeFor(matDeck, _cardPack!), picture);
                else _view.Mat.SetTheme(seat, Playmat.Space(seat), null, "space");
            }
            _view.SetReplayMode(true);
            _view.SetHumanMode(false);
            _view.AlleyBar = false;
            _view.SetDuelTitle(replay == _lessonReplay ? "Replay Lesson" : "Replay");
            _view.SetResponseMode(false, ResponseSetting);
            _view.HiddenSeat = -1;
            _human = null;
            _answerReady = false;

            var narrator = new DuelNarrator(_cardPack!, teamNames[0], teamNames[1]);
            var replayDuel = _duel;
            narrator.CodeAt = place => BoardState.PublicCodeAt(replayDuel, place);
            _narrator = narrator;
            narrator.Line += _view.AppendLog;
            var feed = new ReplayFeed(replay.Responses);
            _runner = new DuelRunner(_duel, new ReplayDuelist(feed), new ReplayDuelist(feed));
            YataLock.Attach(_runner, _duel);
            // An alley duel's replay ends where it did: at the turn cap.
            _turnCap = replay.DeckNames.Any(Alley.IsAlleyDeck) ? TurnCap.Attach(_runner, _duel) : null;
            _view.TurnCapLast = _turnCap?.LastTurn ?? 0;
            _runner.MessageReceived += narrator.Handle;
            var replayHistory = new DuelHistory(_cardPack!, teamNames[0], teamNames[1]) { CodeAt = narrator.CodeAt };
            _history = replayHistory;
            _view.SetHistory(replayHistory);
            _runner.MessageReceived += replayHistory.Handle;
            // Its key moments are found on another thread (the duel is played through once more, unseen).
            if (_momentsFor != replay)
            {
                _momentsFor = replay;
                _moments = null;
                _view.SetMomentsReady(false);
                var cards = _cardPack!;
                var scripts = _scripts;
                System.Threading.Tasks.Task.Run(() => Ygo.Engine.Replays.ReplayMoments.Find(replay, cards, scripts))
                    .ContinueWith(t => { if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) _pendingMoments = (replay, t.Result); });
            }
            WatchMessages();

            _view.AppendLog($"Replay: {replay.Title}\nRecorded {replay.Date:ddd d MMM yyyy, HH:mm}.");
            if (replay == _lessonReplay) _view.SetLesson(_lessonTitle, _lessonSummary, EndLesson);
            else { _lessonReplay = null; _lessonTurn = 0; _view.SetLesson(null, null, null); }
            if (replay.CardCount != 0 && replay.CardCount != _cardPack!.Count)
                _view.AppendLog($"Note: recorded with {replay.CardCount} cards in the database, you have {_cardPack.Count}. If cards changed since, the replay may play out differently.");

            _view.Fx.ArmOpeningShuffle(); // from the start: both Decks riffle before the opening hands (a jump clears it below)
            // Jump: run the duel to that position without drawing or animating it.
            while (_ticks < skipTo && !_runner.IsWaiting && Advance()) { }
            _events.Clear();
            _board.Refresh(_duel);
            if (_board.Winner >= 0 && !_announcedResult) AnnounceResult();
            _view.Render(_board);
                if (_narrator != null) _view.ShowChain(_narrator.Chain.Links);
            if (skipTo > 0) { _paused = true; _view.Fx.Clear(); }
            UpdateControls();
        }

        /// <summary>Runs the engine until something the viewer can see happens. False once the duel is over.</summary>
        private bool Advance()
        {
            if (_runner == null || _runner.IsFinished) return false;
            _sawVisibleEvent = false;
            for (int i = 0; i < MaxStepsPerTick && !_sawVisibleEvent; i++)
                if (!_runner.Step() || _runner.IsWaiting) break;
            _ticks++;
            return true;
        }

        /// <summary>Jumps the replay being watched to a position, and marks what changed since where you were.</summary>
        private void JumpReplay(int tick)
        {
            if (_watching == null) return;
            var before = BoardSnapshot.Of(_board);
            var keep = _moments;
            WatchReplay(_watching, tick);
            _moments = keep;
            _view.SetMomentsReady(keep != null);
            ShowChangesSince(before);
        }

        /// <summary>After a jump: marks on the board, and a line in the log (they clear once the replay plays on).</summary>
        private void ShowChangesSince(BoardSnapshot before)
        {
            var changes = BoardChanges.Between(before, _board);
            _view.ShowChanges(changes.Any ? changes : null);
            _view.AppendLog(changes.Summary(_players.Select(p => p.Name).ToList()));
        }

        private void SkipToNextTurn()
        {
            if (_watching == null || _runner == null) return;
            var before = BoardSnapshot.Of(_board);
            _sawNewTurn = false;
            while (!_sawNewTurn && !_runner.IsWaiting && Advance()) { }
            _events.Clear();
            _view.Fx.Clear();
            _board.Refresh(_duel!);
            if (_board.Winner >= 0 && !_announcedResult) AnnounceResult();
            _view.Render(_board);
                if (_narrator != null) _view.ShowChain(_narrator.Chain.Links);
            _paused = true;
            UpdateControls();
            ShowChangesSince(before);
        }

        private void SaveReplay()
        {
            if (_recording == null) return;
            _recording.Winner = _board.Winner;
            _recording.Turns = _board.Turn;
            _recording.Date = DateTime.Now;
            var path = _replays.Save(_recording);
            var code = _recording.ToCode();
            GUIUtility.systemCopyBuffer = code;
            _view.AppendLog($"Saved replay to {path}. Its code ({code.Length} characters) is on the clipboard to share.");
        }

        private int _soundTurn;

        /// <summary>Whether the story scenes playing started over the alley tab or the van (the rain bed stays on under them).</summary>
        private bool _storyOverAlley;

        /// <summary>Whether this duel plays a boss's theme, their ace has shown up yet, and their decks' playbooks.</summary>
        private bool _bossMusic, _bossAceSeen;
        private Playbook[] _bossPlaybooks = Array.Empty<Playbook>();

        /// <summary>Menu music on any menu screen; in a duel, its theme following how the duel stands.</summary>
        private void UpdateMusic()
        {
            bool menus = _editor.IsOpen || _browser.IsOpen || _campaignScreen.IsOpen || _shop.IsOpen || _menu.IsOpen || _settings.IsOpen || _about.IsOpen
                         || _import.IsOpen || _trader.IsOpen || _recipeBook.IsOpen || _structures.IsOpen || _scenes.IsOpen || _tagSetup.IsOpen || _storyScreen.IsOpen || (_title.IsOpen && !_title.InAttract)
                         || _van.IsOpen;
            Sounds.Music(menus ? "menu" : _runner != null ? "duel" : "menu");
            // The alley's rain bed, under whatever plays, while the Back Alley tab or Moth's van is shown (or a story scene
            // plays over either).
            Sounds.Bed((_campaignScreen.IsOpen && _campaignScreen.InAlley) || _van.IsOpen || (_storyScreen.IsOpen && _storyOverAlley));
            if (_board == null || _board.Winner >= 0) return;
            bool Ace(int side) => _board.Players[side].Monsters.Any(m => m != null && m.IsFaceUp
                && ((m.Level & 0xff) >= 8 || m.Attack >= 3000 || _bossPlaybooks.Any(p => p.IsBoss(m.Code))));
            // A boss's tune stays once their ace has shown up; the everyday arpeggio comes and goes with anyone's ace.
            _bossAceSeen |= Ace(1);
            bool ace = _bossMusic ? _bossAceSeen : Ace(0) || Ace(1);
            // Low: a quarter of the LP or less (where the panels turn red). Yours comes first: it's the one to hear.
            int danger = _board.Players[0].LifePoints <= 2000 ? 1 : _board.Players[1].LifePoints <= 2000 ? 2 : 0;
            Sounds.DuelState(ace, danger, LethalAttack());
        }

        /// <summary>
        /// An attack under way that ends the duel unless something stops it: a direct attack for at least the defender's
        /// LP, or one on a face-up Attack Position monster whose difference is.
        /// </summary>
        private bool LethalAttack()
        {
            var shown = _view.AttackShowing;
            if (shown == null) { _musicAttack = null; return false; }
            // The LP as the attack began: the arrow stays a moment after the damage, which mustn't count again.
            if (!Equals(shown, _musicAttack)) { _musicAttack = shown; _musicAttackLp = _board.Players.Select(p => p.LifePoints).ToArray(); }
            var attack = shown.Value;
            int side = attack.to is { } target ? target.Controller : 1 - attack.from.Controller;
            int lp = _musicAttackLp[side];
            if (_board.Players[side].LifePoints < lp || _view.LiveCard(attack.from) is not { } attacker) return false;
            if (attack.to is not { } to) return attacker.Attack >= lp;
            var defender = _view.LiveCard(new Ygo.Engine.Protocol.CardRef(to.Code, to.Controller, Location.MZone, to.Sequence, 0));
            return defender != null && defender.IsFaceUp && !defender.IsDefense && attacker.Attack - defender.Attack >= lp;
        }

        /// <summary>The attack <see cref="LethalAttack"/> last saw, and each side's LP when it began.</summary>
        private (Ygo.Engine.Protocol.CardRef from, Ygo.Engine.Protocol.CardRef? to)? _musicAttack;
        private int[] _musicAttackLp = new int[2];

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current?.f12Key.wasPressedThisFrame == true) ToggleDevPanel();
#else
            if (Input.GetKeyDown(KeyCode.F12)) ToggleDevPanel();
#endif
            TakeMoments();
            TakeLesson();
            WatchChainLesson();
            UpdateMusic();
            if (_runner == null || _duel == null || _runner.IsFinished || _editor.IsOpen || _browser.IsOpen
                || _campaignScreen.IsOpen || _shop.IsOpen || _menu.IsOpen || _settings.IsOpen || _about.IsOpen || _import.IsOpen || _tagSetup.IsOpen || _van.IsOpen
                || _storyScreen.IsOpen // a story scene holds the duel until it's read
                || (_title.IsOpen && !_title.InAttract)) return;
            if (SkipStep()) return; // skipping the opponent's turn: it runs the duel itself

            // Waiting on you: nothing moves until you answer, then play resumes immediately.
            bool resume = _answerReady;
            if (_runner.IsWaiting && !resume)
            {
                // A replay of a duel someone conceded: its answers end at their decision, and the duel ends as it did.
                if (_watching != null && _watching.Winner is 0 or 1 && _board.Winner < 0)
                {
                    _runner.Concede(1 - _watching.Winner, DuelRunner.ReasonConceded);
                    _answerReady = true;
                    return;
                }
                if (_watching != null && !_shownReplayEnd)
                {
                    _shownReplayEnd = true;
                    _view.AppendLog("-- End of the replay (it was saved before the duel finished) --");
                }
                return;
            }
            if (!resume)
            {
                if (_paused && !_stepRequested) return;
                _timer += Time.deltaTime;
                if (!_stepRequested && _timer < 1f / Speeds[PlaySpeed]) return;
            }
            // Let the last thing that happened finish animating before the next (and a combo's payoff beat, DuelController.Show).
            if (_view.Fx.Busy || ShowHolding) return;
            _timer = 0;
            _stepRequested = false;
            _answerReady = false;

            try
            {
                // Run the engine until something the viewer can see happens, then draw it.
                Advance();
                _view.ShowChanges(null); // the marks from a jump last until the replay plays on
                _board.Refresh(_duel);
                if (_board.Winner >= 0 && !_announcedResult) AnnounceResult();
                _view.Render(_board);
                if (_narrator != null) _view.ShowChain(_narrator.Chain.Links);
                if (_board.Turn != _soundTurn && _view.Fx.Tempo > 0) { _soundTurn = _board.Turn; Sounds.Play("turn", 0.7f); }
                _view.Fx.Play(_events);
                React();
                StepShow();
                _events.Clear();
            }
            catch (Exception ex)
            {
                _paused = true;
                _view.AppendLog($"[engine error] {ex.Message}");
                Debug.LogException(ex);
                UpdateControls();
            }
        }

        private System.Collections.IEnumerator FxShots(string folder)
        {
            Directory.CreateDirectory(folder);
            // 16 events/s: animations still play (about 4.5x shorter), so screenshot timings scale with them.
            speedIndex = 5;
            UpdateControls();
            float pace = _view.Fx.Tempo;
            var wanted = new HashSet<string> { "draw", "move", "summon", "spotlight", "attack", "damage", "phase", "turn", "cutin", "finalblow",
                "material", "shatter", "banish", "scene-fusion", "scene-synchro", "scene-xyz", "scene-link", "scene-ritual" };
            var pending = new Queue<(string kind, float at)>();
            _view.Fx.Started += kind =>
            {
                if (!wanted.Remove(kind)) return;
                var delay = kind.StartsWith("scene") ? 0.5f : kind == "material" ? 0.25f : kind == "banish" ? 0.2f : kind == "shatter" ? 0.18f
                    : kind == "spotlight" ? 0.4f : kind == "phase" || kind == "turn" || kind == "cutin" ? 0.35f : kind == "finalblow" ? 0.5f : kind == "attack" ? 0.22f : kind == "damage" ? 0.12f : 0.1f;
                pending.Enqueue((kind, Time.unscaledTime + delay * pace));
                if (kind == "damage") pending.Enqueue(("damage-late", Time.unscaledTime + 0.45f * pace));
            };
            foreach (var r in new[] { "react-summon", "react-strike", "react-hit", "react-danger", "chainpanel", "floater", "negated", "set-this-turn" }) wanted.Add(r);
            _view.ChainShown += links => { if (wanted.Remove("chainpanel")) pending.Enqueue(("chainpanel", Time.unscaledTime + 0.3f * pace)); };
            _view.NegatedShown += () => { if (wanted.Remove("negated")) pending.Enqueue(("negated", Time.unscaledTime + 0.1f)); };
            _view.SetThisTurnShown += () => { if (wanted.Remove("set-this-turn")) pending.Enqueue(("set-this-turn", Time.unscaledTime + 0.1f)); };
            _view.Reacted += kind =>
            {
                if (wanted.Remove("react-" + kind)) pending.Enqueue(("react-" + kind, Time.unscaledTime + 0.5f * pace));
            };
            var deadline = Time.realtimeSinceStartup + 150;
            bool tensionSeen = false;
            float lowestDuck = 1;
            while ((wanted.Count > 0 || pending.Count > 0) && Time.realtimeSinceStartup < deadline)
            {
                tensionSeen |= Sounds.DangerOn;
                lowestDuck = Mathf.Min(lowestDuck, Sounds.Duck);
                if (pending.Count > 0 && Time.unscaledTime >= pending.Peek().at)
                {
                    var (kind, _) = pending.Dequeue();
                    var path = Path.Combine(folder, $"fx-{kind}.png");
                    ScreenCapture.CaptureScreenshot(path);
                    Debug.Log($"fx shot: {path}");
                }
                if (_runner != null && _runner.IsFinished) { seed++; NewDuel(); }
                yield return null;
            }
            Debug.Log($"fx shots done; missing: {string.Join(", ", wanted)}");
            Debug.Log($"fx repeats: {_view.Fx.FullMoments} big moments in full, {_view.Fx.ShortMoments} shortened");
            Debug.Log($"fx music: low-LP layers {(tensionSeen ? "came on" : "never")}, lowest dip {lowestDuck:P0}");
            Debug.Log($"fx sounds played: {string.Join(", ", Sounds.Played.OrderByDescending(p => p.Value).Select(p => $"{p.Key} x{p.Value}"))}");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private System.Collections.IEnumerator ReplayCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            void Report(string what, bool ok) => Debug.Log($"replay check: {(ok ? "ok  " : "FAIL")} {what}");
            string Snapshot() => $"turn {_board.Turn} winner {_board.Winner} LP {_board.Players[0].LifePoints}/{_board.Players[1].LifePoints} "
                                 + string.Join(",", _board.Players.SelectMany(p => p.Monsters.Concat(p.SpellTraps)).Select(c => c?.Code ?? 0));

            // 1. A live duel, played out quickly.
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            var deadline = Time.realtimeSinceStartup + 120;
            while (!_announcedResult && Time.realtimeSinceStartup < deadline) yield return null;
            var live = Snapshot();
            var liveTicks = _ticks;
            var recent = _replays.All().Where(e => e.Recent).ToList();
            Report($"finished duel was saved to recent replays ({recent.Count} found)", recent.Count > 0);
            if (recent.Count == 0) { Application.Quit(); yield break; }

            // 2. The replay list.
            _browser.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "replays-list.png"));
            yield return null;
            _browser.Close();

            // 3. Watch it to the end (at full speed) and compare.
            var replay = recent[0].Replay;
            WatchReplay(replay, 0);
            _paused = false;
            UpdateControls();
            deadline = Time.realtimeSinceStartup + 120;
            while (!_announcedResult && Time.realtimeSinceStartup < deadline) yield return null;
            yield return null;
            var watched = Snapshot();
            Report($"replay ends exactly like the duel ({live} | {watched})", live == watched && _ticks == liveTicks);

            // 4. Jump to the middle, step forward, then back: the board must match.
            WatchReplay(replay, liveTicks / 2);
            var middle = Snapshot();
            Advance();
            _board.Refresh(_duel!);
            WatchReplay(replay, Math.Max(0, _ticks - 1));
            Report($"stepping back returns to the same position (tick {_ticks})", Snapshot() == middle);

            // 5. Next turn moves to a later turn.
            var turn = _board.Turn;
            SkipToNextTurn();
            Report($"next turn goes from turn {turn} to {_board.Turn}", _board.Turn == turn + 1 || _board.Winner >= 0);

            // 6. Key moments: found in the background; picking one jumps to just before it.
            var momentsDeadline = Time.realtimeSinceStartup + 30;
            while (_moments == null && Time.realtimeSinceStartup < momentsDeadline) yield return null;
            Report($"the replay's key moments are found ({_moments?.Count ?? 0})", _moments != null && _moments.Count > 0);
            if (_moments != null)
            {
                _view.ShowMoments(_moments, _ticks);
                yield return new WaitForSecondsRealtime(0.4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "replay-moments.png"));
                yield return new WaitForSecondsRealtime(0.2f);
                var pick = _moments.LastOrDefault(m => m.Kind == MomentKind.Turn) ?? _moments[0];
                _view.CloseMoments();
                var turnBefore = _board.Turn;
                JumpReplay(pick.Tick);
                Report($"jumping to \"{pick.Text}\" lands at tick {_ticks} of {pick.Tick}", _ticks == pick.Tick);
                yield return new WaitForSecondsRealtime(0.4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "replay-changes.png"));
                yield return null;
                Report($"the jump from turn {turnBefore} marks what changed ({_view.ChangeMarks} marks)", _view.ChangeMarks > 0 || pick.Turn == turnBefore);
                Advance();
                _board.Refresh(_duel!);
                _view.ShowChanges(null);
                _view.Render(_board);
                Report("the marks clear once the replay plays on", _view.ChangeMarks == 0);
                JumpReplay(pick.Tick);
            }

            // Whose hands show: both, then one, then the other.
            var handsSeen = new List<string>();
            for (int i = 0; i < 3; i++) { handsSeen.Add(System.Text.RegularExpressions.Regex.Replace(_view.HandsText, "<[^>]+>", "")); if (i == 1) { yield return new WaitForSecondsRealtime(0.3f); ScreenCapture.CaptureScreenshot(Path.Combine(folder, "replay-one-hand.png")); yield return null; } _view.CycleHands(); }
            Report($"the hands pill cycles ({string.Join(" | ", handsSeen)})", handsSeen.Distinct().Count() == 3 && _view.HandsMode == 0);

            // A code copied mid-replay opens at the same spot.
            var hereTicks = _ticks;
            var here = Snapshot();
            var atCode = _watching!.ToCode(hereTicks);
            var opened = Replay.FromCode(atCode);
            if (opened != null) WatchReplay(opened, opened.OpenAt);
            Report($"a code copied at tick {hereTicks} opens there ({opened?.OpenAt})", opened != null && opened.OpenAt == hereTicks && Snapshot() == here);
            WatchReplay(replay, hereTicks);

            // 7. Watch a bit at normal speed for screenshots of the replay screen.
            speedIndex = 3;
            _paused = false;
            UpdateControls();
            yield return new WaitForSecondsRealtime(2.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "replay-watching.png"));
            yield return null;

            // 7. A code round trip, through the clipboard like a player would.
            var code = replay.ToCode();
            var back = Replay.FromCode("  " + code.Insert(40, "\n") + "  ");
            Report($"code ({code.Length} chars) reads back with the same moves", back != null && back.Responses.Count == replay.Responses.Count);

            // 8. Mats (replay format 5): a mat the duel chose by name (your era mat, a table's) rides in the code and shows on
            // playback; mats rebuilt from the seats record nothing. A duel that shuffles after a Deck look
            // (DuelFlags.ShuffleAfterDeckLook, every new duel) is written as format 6 either way, which a build before it
            // refuses; one without the flag is still written as format 4 (no mat) or 5 and still loads and plays.
            bool shuffles = (replay.Settings.Flags & DuelFlags.ShuffleAfterDeckLook) != 0;
            Report($"a new duel shuffles after a Deck look (flags 0x{replay.Settings.Flags:x})", shuffles);
            Report($"a duel whose mats come from its seats records none ({string.Join(" | ", replay.Mats.Select(m => m.Length == 0 ? "-" : m))})", replay.Mats.All(m => m.Length == 0));
            var matKeys = Playmat.Signatures.Keys.Where(k => k.StartsWith("era-")).Take(2).ToList();
            if (matKeys.Count == 2 && back != null)
            {
                back.Mats[0] = matKeys[0];
                back.Mats[1] = matKeys[1];
                var mattedCode = back.ToCode();
                var matted = Replay.FromCode(mattedCode);
                if (matted != null) WatchReplay(matted, 0);
                yield return new WaitForSecondsRealtime(0.4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, "replay-mats.png"));
                yield return null;
                bool Shows(int seat) => _view.Mat.ThemeOf(seat).Accent == Playmat.Signatures[matKeys[seat]].Theme.Accent;
                Report($"recorded mats ({matKeys[0]}, {matKeys[1]}) are written as format {CodeVersion(mattedCode)}, read back from the code and show on playback",
                    CodeVersion(mattedCode) == (shuffles ? 6 : 5) && matted != null && matted.Mats.SequenceEqual(matKeys) && Shows(0) && Shows(1));
            }
            else Report("two era mats in mats.txt for the mat step", false);
            var plainCode = replay.ToCode();
            var old = Replay.FromCode(plainCode);
            if (old != null) WatchReplay(old, 0);
            Report($"with no mat recorded the code is format {CodeVersion(plainCode)}; it loads ({old?.Responses.Count} answers) with default mats, and plays",
                CodeVersion(plainCode) == (shuffles ? 6 : 4) && old != null && old.Responses.Count == replay.Responses.Count && old.Mats.All(m => m.Length == 0) && _watching == old);

            // A replay without the flag (recorded before it): written as format 4, or 5 with a mat (the last build reads
            // both), keeps its flags through the code, and plays back exactly: the same position twice over.
            var unflagged = Replay.FromCode(replay.ToCode());
            if (unflagged != null)
            {
                unflagged.Settings.Flags &= ~DuelFlags.ShuffleAfterDeckLook;
                var unflaggedCode = unflagged.ToCode();
                var unflaggedBack = Replay.FromCode(unflaggedCode);
                int mattedVersion = -1;
                if (matKeys.Count == 2)
                {
                    var unflaggedMatted = Replay.FromCode(unflaggedCode)!;
                    unflaggedMatted.Mats[0] = matKeys[0];
                    unflaggedMatted.Mats[1] = matKeys[1];
                    mattedVersion = CodeVersion(unflaggedMatted.ToCode());
                }
                string first = "", second = "";
                int firstTicks = -1, secondTicks = -2;
                if (unflaggedBack != null)
                {
                    WatchReplay(unflaggedBack, liveTicks);
                    first = Snapshot();
                    firstTicks = _ticks;
                    WatchReplay(Replay.FromCode(unflaggedCode)!, liveTicks);
                    second = Snapshot();
                    secondTicks = _ticks;
                }
                Report($"a replay without the flag is format {CodeVersion(unflaggedCode)} ({mattedVersion} with a mat), keeps its flags (0x{unflaggedBack?.Settings.Flags:x}) and plays back the same twice (tick {firstTicks}: {first}; as the live duel: {first == live})",
                    CodeVersion(unflaggedCode) == 4 && mattedVersion == 5 && unflaggedBack != null
                    && (unflaggedBack.Settings.Flags & DuelFlags.ShuffleAfterDeckLook) == 0 && unflaggedBack.Responses.Count == replay.Responses.Count
                    && first.Length > 0 && first == second && firstTicks == secondTicks);
            }
            else Report("the replay reads back for the unflagged step", false);
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// <summary>For the replay check: the format version inside a replay code (-1 when it isn't one).</summary>
        private static int CodeVersion(string code)
        {
            const string prefix = "YGOR1:";
            if (!code.StartsWith(prefix)) return -1;
            var s = code.Substring(prefix.Length);
            if (s.IndexOf('@') is int at && at >= 0) s = s.Substring(0, at);
            s = s.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            using var raw = new MemoryStream(Convert.FromBase64String(s));
            using var inflate = new System.IO.Compression.DeflateStream(raw, System.IO.Compression.CompressionMode.Decompress);
            var head = new byte[8];
            int read = 0;
            while (read < head.Length && inflate.Read(head, read, head.Length - read) is int n && n > 0) read += n;
            return read == head.Length ? BitConverter.ToInt32(head, 4) : -1;
        }

        private System.Collections.IEnumerator CampaignCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"campaign-{name}.png"));
                yield return null; // taken at the end of this frame
                yield return null;
            }
            void Report(string what, bool ok) => Debug.Log($"campaign check: {(ok ? "ok  " : "FAIL")} {what}");
            yield return new WaitForSecondsRealtime(0.5f);

            // A fresh save: the starter choice first.
            Report("no save yet: the campaign asks for a starter deck", _campaign.Profile == null);
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("starters");
            Report($"every starter tile has its deck, and the classic ones their Historic Rules edition ({CampaignState.Starters.Length} starters)",
                CampaignState.Starters.All(st => _library.Contains(st.Deck) && (st.HistoricDeck.Length == 0 || _library.Contains(st.HistoricDeck))));
            Report("the era option explains itself when highlighted", _campaignScreen.DebugHoverEraOption().Contains("era-limited"));
            yield return Shot("starters-option");
            _campaign.Start(CampaignState.Starters[1]); // Warrior's Charge: Reinforcement of the Army is limited by 2011
            var profile = _campaign.Profile!;
            Report($"starter deck {profile.Deck} is a legal campaign deck ({profile.Collection.Total} cards, {profile.Dp} DP)",
                _library.IsCampaign(profile.Deck) && _campaign.Problems(_library.Load(profile.Deck)).Count == 0);
            Report("every card in the starter collection is at most 3 copies", profile.Collection.Codes.All(c => profile.Collection.Count(c) <= 3));
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("circuits");
            // The whole climb, a screen at a time, top first.
            for (int part = 0; part < 5; part++)
            {
                _campaignScreen.DebugScrollMap(1 - part / 4f);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot($"map-{part}");
            }
            // The header's wallet with long values at its narrowest (every header button shown) fits without wrapping.
            var (walletSize, walletWidest, walletRoom) = _campaignScreen.DebugLongWallet();
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("header-long");
            Report($"the header's wallet fits long values: widest line {walletWidest:0} of {walletRoom:0} px at size {walletSize} (level 99, NG+12, 9,999,999 DP, every button shown)",
                walletWidest <= walletRoom && walletSize >= 14);
            _campaignScreen.Close();

            int cards = profile.Collection.Total, dpBefore = profile.Dp;
            yield return _shop.DebugOpenPack(0, name => ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"campaign-{name}.png")));
            Report($"a pack opened: {profile.Collection.Total - cards} cards kept, DP {dpBefore} -> {profile.Dp}", profile.Collection.Total > cards);
            // Gauge & Gilt: the desk's three tabs, an express grading and its reveal.
            DebugAddDp(profile, 20000);
            _gradingDesk.DebugShow(0);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("grading-desk");
            int slabbed = _gradingDesk.DebugGradeBest();
            yield return new WaitForSecondsRealtime(2.4f);
            yield return Shot("slab-reveal");
            Report($"the Grading Desk grades a copy express and reveals its slab ({slabbed}: {profile.Collection.All.Count(c => c.IsSlab)} slab in the collection)",
                slabbed >= 1 && slabbed <= 10 && _gradingDesk.DebugRevealing && profile.Collection.All.Count(c => c.IsSlab) == 1);
            _gradingDesk.DebugFinishReveal();
            yield return new WaitForSecondsRealtime(0.4f);
            int pays = _gradingDesk.DebugPickSale();
            _gradingDesk.DebugShow(1);
            _gradingDesk.DebugPickSale();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("sell-desk");
            _gradingDesk.DebugShow(2);
            _gradingDesk.DebugPickListing();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("used-market");
            Report($"the Sell desk offers a copy ({pays} DP) and the used market lists {_campaign.MarketListings().Count} copies",
                _campaign.MarketListings().Count == UsedMarket.Size);
            // Card versions (phase 2): each listing printing an original text is tagged "ORIGINAL TEXT · <year>".
            var marketNow = _campaign.MarketListings();
            Report($"the used market tags {_gradingDesk.DebugOriginalBands} listing(s) ORIGINAL TEXT ({marketNow.Count(l => CardDetail.CarriesOriginal(l.Copy))} print one; "
                   + $"{marketNow.Count(l => l.Copy.Set.Length > 0)} of {marketNow.Count} have a printing)",
                _gradingDesk.DebugOriginalBands == marketNow.Count(l => CardDetail.CarriesOriginal(l.Copy)));
            // A later period whose listings print an original text (looked ahead by the duel count, then put back).
            {
                int duelsBefore = profile.DuelsPlayed, epochBefore = profile.MarketEpoch;
                var soldBefore = profile.MarketSold.ToList();
                var ahead = marketNow;
                for (int k = 1; k <= 80 && ahead.All(l => !CardDetail.CarriesOriginal(l.Copy)); k++)
                {
                    profile.DuelsPlayed = duelsBefore + k * UsedMarket.Every;
                    ahead = _campaign.MarketListings();
                }
                var orig = ahead.FirstOrDefault(l => CardDetail.CarriesOriginal(l.Copy));
                if (orig != null)
                {
                    _gradingDesk.DebugPickListing(orig.Slot);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return Shot("used-market-original");
                }
                var originalDetail = _gradingDesk.DebugDetailText();
                Report($"a period {profile.DuelsPlayed - duelsBefore} duels on, the used market lists {ahead.Count(l => CardDetail.CarriesOriginal(l.Copy))} original-text cop(ies) "
                       + $"({(orig == null ? "-" : $"{_cardPack!.Get(orig.Copy.Code)?.Name} {orig.Copy.Set} {orig.Copy.Printed}")}): {_gradingDesk.DebugOriginalBands} tagged; the detail reads its printing",
                    orig != null && _gradingDesk.DebugOriginalBands == ahead.Count(l => CardDetail.CarriesOriginal(l.Copy))
                    && originalDetail.Contains($"{orig.Copy.Set} · {orig.Copy.Printed.Substring(0, 4)} · ORIGINAL TEXT"));
                profile.DuelsPlayed = duelsBefore;
                profile.MarketEpoch = epochBefore;
                profile.MarketSold.Clear();
                foreach (var slot in soldBefore) profile.MarketSold.Add(slot);
            }
            _gradingDesk.Close();
            _shop.Close();
            _campaignScreen.Close();
            var (keptPulls, chips) = _shop.DebugConditions();
            Report($"every kept pull shows its condition ({chips} of {keptPulls}: "
                   + string.Join(", ", profile.Collection.All.Where(c => c.Source is CopySource.Pack or CopySource.Foil).GroupBy(c => Grades.Word(c.Grade)).Select(g => $"{g.Count()} {g.Key}")) + ")",
                keptPulls > 0 && chips == keptPulls);
            _shop.Close();
            _campaignScreen.Close();

            _editor.Open(profile.Deck, profile.Collection);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("collection-editor");
            // The catalog shows every card: yours first, the rest dimmed with where to get them.
            var (allCards, unowned, _) = _editor.DebugCatalog();
            Report($"the campaign catalog shows cards you don't own too ({unowned} of {allCards})", unowned > 0 && unowned < allCards);
            _editor.DebugFilters(kind: 1, attribute: 6, sort: 3, chip: -1); // DARK monsters by ATK
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("catalog-filters");
            var (darkCards, _, activeChips) = _editor.DebugCatalog();
            Report($"pickers filter to DARK monsters sorted by ATK ({darkCards} cards, {activeChips} active filter chips)", darkCards > 0 && activeChips >= 2);
            _editor.DebugClearFilters();
            _editor.DebugRelated("Dark Magician");
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("catalog-related");
            var (related, _, _) = _editor.DebugCatalog();
            Report($"Related cards for Dark Magician finds {related}", related >= 10);
            Report("clicking the card text opens the reading view", _editor.DebugReading());
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("catalog-reading");
            _editor.DebugClearFilters();
            // Extras: favorites, the deck's archetype and key cards, the Extra Deck check.
            int favorites = _editor.DebugFavorite();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("editor-favorites");
            Report($"a starred card shows under Favorites ({favorites})", favorites >= 1);
            _editor.DebugUnfavoriteAll();
            var analysis = _editor.DebugAnalysis();
            Report($"the deck analysis reads: {analysis.Replace("\n", " | ")}", true);
            // Safety: every change is autosaved, and reopening offers it back.
            Report("a change is autosaved", _editor.DebugAddAny());
            _editor.DebugOfferRecovery();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("editor-recovery");
            Report("unsaved changes are offered back", _editor.DebugRecoveryOffered);
            // Labels as folders, Copy, and the deck browser.
            _editor.DebugLabelAndCopy("Test folder");
            var folders = _editor.DebugBrowser();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("editor-browser");
            Report($"Copy saves a labelled copy, and the deck browser shows it in its folder ({string.Join(", ", folders.Select(f => f.Split('<')[0].Trim()))})",
                folders.Any(f => f.StartsWith("TEST FOLDER")));
            // Saving over most of a deck asks first; Save as new keeps the old one and makes the new one your deck.
            var playing = profile.Deck;
            var copied = _editor.CurrentDeckName;
            var (asked, savedAs) = _editor.DebugReplaceAndSaveAsNew();
            yield return new WaitForSecondsRealtime(0.3f);
            Report($"Save over most of {copied} asks overwrite or save as new; saved as {savedAs}, now the campaign deck, {copied} kept",
                asked && savedAs != copied && _library.Contains(savedAs) && _library.Contains(copied) && _library.Load(copied).Main.Count > 0 && profile.Deck == savedAs);
            // Saving again with nothing changed says so; saving the same cards as new again opens the deck that has them.
            int decksBefore = _library.Names.Count;
            var (askedAgain, unchanged) = _editor.DebugSaveUnchanged();
            Report($"Save with nothing changed makes no copy: {unchanged}", !askedAgain && unchanged.StartsWith("Nothing to save") && _library.Names.Count == decksBefore);
            var (askedTwice, savedTwice, twiceStatus) = _editor.DebugOpenReplaceAndSaveAsNew(copied);
            Report($"Save as new of cards already saved makes no copy: {twiceStatus}",
                askedTwice && savedTwice == savedAs && _library.Names.Count == decksBefore && profile.Deck == savedAs && _library.Load(copied).Main.Count > 0);
            // A rebuild with the same cards but other starters (Build clears them, a Recipe brings its own) still asks first.
            var (askedStarters, startersKept) = _editor.DebugRebuildKeepsStarters(copied);
            Report($"Save after a rebuild that keeps the cards but drops the marked starters asks first; the saved deck keeps {startersKept} starter(s)",
                askedStarters && startersKept == 1);
            profile.Deck = playing;
            _campaign.Save();
            // Era-limited: a card from a later format isn't allowed.
            var firstEra = Enumerable.Range(0, _campaign.Circuits.All.Count).First(i => _campaign.CircuitList(i) != null);
            var future = _library.Load(profile.Deck);
            future.Main[0] = _cardPack!.All.First(c => c.Name == "Ash Blossom & Joyous Spring" && c.Alias == 0).Code;
            var notOut = _campaign.Problems(future, firstEra).Where(p => p.Message.Contains("not released yet")).ToList();
            Report($"in an era-limited campaign a card from later is flagged ({notOut.FirstOrDefault()?.Message})", !profile.EraLimited || notOut.Count == 1);
            _editor.Close();
            _campaignScreen.Close();

            // The first match of the first circuit, played with random clicks at full speed.
            var dp = profile.Dp;
            ShowVersus(0, 0);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("versus");
            Report("the versus screen offers Duel!", _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!"));
            // For this duel only, every copy in your deck is a Gem Mint Secret Rare: one landing face-up has its prized
            // moment (a screenshot mid-sweep). The copies are put back after the duel.
            var realCopies = profile.Collection.All.Select(c => (copy: c, c.Rarity, c.Grade, c.Slab)).ToList();
            foreach (var c in profile.Collection.All) { c.Rarity = Rarity.SecretRare; c.Grade = 10; c.Slab = 10; }
            int prizedBefore = _view.Fx.PrizedPlayed, arrivalsBefore = _view.ArrivalsSeen;
            int speedBefore = speedIndex;
            speedIndex = 4; // 8 events a second: quick, but animations (and moments) still play
            UpdateControls();
            bool prizeShot = false;
            void OnPrize(string kind)
            {
                if (kind != "prize" || prizeShot) return;
                prizeShot = true;
                StartCoroutine(PrizeShot());
            }
            System.Collections.IEnumerator PrizeShot()
            {
                yield return new WaitForSecondsRealtime(0.55f);
                yield return Shot("prized-moment");
            }
            _view.Fx.Started += OnPrize;
            // This duel shows a Library era mat on your side, as one put on in the Locker would (the 2005 mat; off after).
            string matBeforeDuel = profile.Mat;
            bool hadGoatMat = !profile.Mats.Add("era-goat-cup");
            profile.Mat = "era-goat-cup";
            _scenes.Buttons["Duel!"]();
            var liveFromDuel = Profile.Load(_campaign.SavePath)?.LiveDuel; // written at Duel! (DuelController.LiveDuel)
            // The campaign layout: wait for your first decision, show a card's details, then open the log.
            var layoutDeadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < layoutDeadline && (_view.Prompt.Current == null || _runner == null || !_runner.IsWaiting)) yield return null;
            var shownCard = _view.Prompt.Current?.Choices.FirstOrDefault(c => c.Card != null)?.Card;
            if (shownCard is { } sc && _cardPack!.Get(sc.Code) is { } shownInfo) _view.ShowInfo(shownInfo);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("duel-layout");
            // Your cards are your copies: each card in your hand carries its deck slot and shows that copy.
            var hand = _duel!.QueryLocation(0, Ygo.Engine.Location.Hand).OfType<FieldCard>().ToList();
            var handLooks = hand.Select(c => _duel.Looks?.Of(c)).ToList();
            Report($"your {hand.Count} hand cards are your own copies ({string.Join(", ", handLooks.OfType<CopyLook>().Select(l => Grades.Word(l.Grade)).Distinct())}), "
                   + "and the replay will show the same ones",
                hand.Count > 0 && handLooks.All(l => l != null) && CardFoil.DuelLooks == _duel.Looks && _recording?.Looks[0] != null);
            if (_cardPack!.Get(89631139) is { } otherInfo) // Blue-Eyes White Dragon
            {
                _view.TogglePin();
                _view.ShowInfo(otherInfo);
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("duel-pinned");
                Report("a pinned card in the campaign layout keeps its place with the hovered one beside it", _view.PinState.pinned != null && _view.PinState.hovered != null);
                _view.TogglePin();
            }
            _view.ToggleLog();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("duel-log");
            Report("campaign duels use the campaign layout, and the log opens over the panel", _view.LogOpen);
            _view.ToggleLog();
            // The full-width board shows the decision list only for decisions it can't show itself.
            var waiting = _view.Prompt.Current;
            Report($"on the full-width board your turn needs no list ({waiting?.Title})", !_view.PromptListShown || waiting == null);
            var declare = new ChoiceSet { Title = "Declare a number", Prompt = new Ygo.Engine.Protocol.AnnounceNumberPrompt() };
            foreach (var n in new[] { "1", "2", "3" }) declare.Choices.Add(new Choice { Label = n });
            _view.ShowChoices(declare, false);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("duel-list-fallback");
            Report("a decision the board can't show brings up the list", _view.PromptListShown);
            _view.ShowChoices(waiting, false);
            yield return DevModeCampaignCheck(Report, Shot);
            // Animated (8/s) until a prized copy has had its moment (or turn 8), then as fast as it goes.
            speedIndex = 4;
            UpdateControls();
            var clicker = StartCoroutine(AutoClick());
            var deadline = Time.realtimeSinceStartup + 240;
            while (!_announcedResult && Time.realtimeSinceStartup < deadline)
            {
                if (speedIndex != Speeds.Length - 1 && (_view.Fx.PrizedPlayed > prizedBefore || _board.Turn >= 8))
                {
                    yield return new WaitForSecondsRealtime(prizeShot ? 1.2f : 0);
                    speedIndex = Speeds.Length - 1;
                    UpdateControls();
                }
                yield return null;
            }
            StopCoroutine(clicker);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("result");
            Report($"its replay records the era mat on your side ({_recording?.Mats[0]}), so playback shows it",
                _recording?.Mats[0] == "era-goat-cup" && _replays.All().FirstOrDefault(e => e.Recent)?.Replay.Mats[0] == "era-goat-cup");
            profile.Mat = matBeforeDuel;
            if (!hadGoatMat) profile.Mats.Remove("era-goat-cup");
            var saved = Profile.Load(_campaign.SavePath);
            Report($"campaign duel finished (winner {_board.Winner}) and paid {profile.Dp - dp} DP; the save has {saved?.Dp} DP",
                _announcedResult && profile.Dp > dp && saved?.Dp == profile.Dp);
            Report($"its Duel! put it live in the save (liveduel {liveFromDuel?.Kind} {liveFromDuel?.Event} {liveFromDuel?.Match}), and its result's save cleared it",
                liveFromDuel is { Kind: LiveDuels.Circuit, Event: 0, Match: 0 } && saved != null && saved.LiveDuel == null && profile.LiveDuel == null);
            // Copies: the save keeps every copy (grade, seed, id), and the duel's replay keeps the looks of the ones you played.
            var looks = _recording == null ? null : Replay.FromCode(_recording.ToCode())?.Looks[0];
            var played = _recording == null ? new List<CardCopy?>() : profile.Collection.Assign(_recording.Decks[0], _cardPack!);
            string Copies(Collection c) => string.Join(";", c.All.OrderBy(x => x.Id).Select(x => $"{x.Code}/{x.Rarity}/{x.Grade}/{x.Seed}"));
            _view.Fx.Started -= OnPrize;
            speedIndex = speedBefore;
            UpdateControls();
            Report($"a Gem Mint Secret Rare of yours landing face-up had its prized moment ({_view.Fx.PrizedPlayed - prizedBefore} this duel, once per copy; "
                   + $"{_view.ArrivalsSeen - arrivalsBefore} arrivals seen; dropped {_view.Fx.PrizesFastForwarded} fast-forwarding, {_view.Fx.PrizesOff} with the setting off; tempo {_view.Fx.Tempo:0.00})",
                _view.Fx.PrizedPlayed > prizedBefore && _view.Prized.Count == _view.Fx.PrizedPlayed - prizedBefore);
            Report($"the save keeps all {profile.Collection.Total} copies as they are, and the replay the looks of {looks?.Count(l => l.IsCopy) ?? 0} of the "
                   + $"{played.Count} cards you played ({string.Join(", ", played.OfType<CardCopy>().GroupBy(c => c.Grade).OrderByDescending(g => g.Key).Select(g => $"{g.Count()} at {g.Key}"))})",
                saved != null && Copies(saved.Collection) == Copies(profile.Collection) && looks != null && looks.Length == played.Count
                && looks.Zip(played, (l, c) => c == null ? !l.IsCopy : l.Grade == c.Grade && l.Seed == c.Seed && l.Rarity == c.Rarity).All(x => x));
            foreach (var (copy, rarity, grade, slab) in realCopies) { copy.Rarity = rarity; copy.Grade = grade; copy.Slab = slab; }
            _campaign.Save();
            var shown = Time.realtimeSinceStartup + 6;
            while (!_scenes.IsOpen && Time.realtimeSinceStartup < shown) yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("results");
            Report("the results screen follows the duel", _scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue"));
            if (_scenes.Buttons.TryGetValue("Continue", out var next)) next();
            yield return new WaitForSecondsRealtime(0.5f);
            // A first win can raise a story (a rival's gift): read it through, as a player would.
            for (int i = 0; i < 60 && _storyScreen.IsOpen; i++) { _storyScreen.DebugFinish(); yield return new WaitForSecondsRealtime(0.1f); }
            yield return new WaitForSecondsRealtime(0.3f);
            Report("Continue goes back to the circuits", _campaignScreen.IsOpen);
            yield return Shot("after-duel");

            // A win that clears the last Beginner circuit: the results screen announces what opens next.
            var lastBeginner = _campaign.Circuits.All.ToList().FindLastIndex(c => c.Band == "Beginner");
            for (int ci = 0; ci < lastBeginner; ci++)
                for (int mi = 0; mi < _campaign.Circuits.All[ci].Matches.Count; mi++) profile.MatchWins[Profile.MatchKey(ci, mi)] = 1;
            var lastMatch = _campaign.Circuits.All[lastBeginner].Matches.Count - 1;
            for (int mi = 0; mi < lastMatch; mi++) profile.MatchWins[Profile.MatchKey(lastBeginner, mi)] = 1;
            int shopYear = _campaign.Circuits.ShopYear(profile);
            var bossId = _campaign.Circuits.All[lastBeginner].Matches[lastMatch].Character.Id;
            bool firstWinOnBoss = profile.WinsAgainst(bossId) == 0;
            var clear = _campaign.Circuits.Record(profile, lastBeginner, lastMatch, 0);
            // Its card unlocks, as after a played duel (no tally: no bounty from a recorded win).
            _campaign.AfterDuel(clear, _campaign.Circuits.All[lastBeginner].Band, null, false, false);
            _campaign.Save();
            _campaignScreen.Close();
            _campaignMatch = (lastBeginner, lastMatch);
            BeginCampaignResults(lastBeginner, lastMatch, clear, shopYear, 0, false);
            for (float until = Time.realtimeSinceStartup + 4; !_scenes.IsOpen && Time.realtimeSinceStartup < until;) yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("results-unlock");
            var news = _scenes.LastResults?.Unlocks ?? new List<string>();
            Report($"clearing {_campaign.Circuits.All[lastBeginner].Name} announces: {string.Join(" | ", news)}",
                news.Any(n => n.Contains("cleared")) && news.Any(n => n.StartsWith("New circuit")) && news.Any(n => n.Contains("Card Shop"))
                && news.Any(n => n.Contains("ban list")) == profile.EraLimited);
            Report($"beating {_campaign.Circuits.All[lastBeginner].Matches[lastMatch].Character.Name} the first time unlocks their recipe",
                news.Any(n => n.StartsWith("Recipe unlocked")));
            if (firstWinOnBoss) CheckRivalGift(Report, profile, clear, bossId);
            else Report($"{bossId} was beaten before (the played duel): a later win pays no second gift", clear.Grants.All(g => g.Kind != GrantKind.RivalGift || g.Character != bossId));
            _scenes.Close();
            _campaignMatch = null;

            // The Recipe Book: Beginner recipes unlocked, a boss's deck in detail, a locked one, and Build.
            _recipeBook.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            int recipeTotal = _recipeBook.DebugCount(out int recipeUnlocked);
            Report($"the Recipe Book has {recipeTotal} recipes, {recipeUnlocked} unlocked after the Beginner band", recipeTotal >= 40 && recipeUnlocked >= 4);
            var bossDeck = _campaign.Circuits.All[lastBeginner].Matches[lastMatch].Deck;
            Report($"the {bossDeck} recipe is unlocked", _recipeBook.DebugSelect(bossDeck));
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("recipe-book");
            Report("a later boss's recipe is locked", !_recipeBook.DebugSelect("burning-abyss"));
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("recipe-locked");
            var deckBefore = profile.Deck;
            _recipeBook.DebugSelect(bossDeck);
            _recipeBook.DebugBuild();
            yield return new WaitForSecondsRealtime(0.5f);
            Report($"Build makes a campaign deck from the recipe ({profile.Deck})", profile.Deck.Contains("(recipe") && _library.IsCampaign(profile.Deck) && _editor.IsOpen);
            Report($"and it plays by the recipe's playbook ({Playbooks.RecipeOf(_library.Load(profile.Deck))})",
                _library.Contains(profile.Deck) && Playbooks.RecipeOf(_library.Load(profile.Deck)) == bossDeck);
            yield return Shot("recipe-built");
            _editor.Close();
            // A Replay Lesson: the recipe's owner plays it, and it opens at their key turn.
            _campaignScreen.Close();
            var lessonRecipe = RecipeBook.All(_campaign.Circuits).First(r => r.Deck == bossDeck);
            PrepareLesson(lessonRecipe);
            var lessonDeadline = Time.realtimeSinceStartup + 90;
            while (_lessonReplay == null && Time.realtimeSinceStartup < lessonDeadline) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("recipe-lesson");
            Report($"the lesson for {lessonRecipe.Owner.Name}'s {bossDeck} opens at turn {_board.Turn} ({_lessonSummary})",
                _lessonReplay != null && _watching == _lessonReplay && (_board.Turn == _lessonTurn - 1 || _board.Turn == _lessonTurn));
            if (_lessonReplay != null) EndLesson();
            _recipeBook.Close();
            _campaignScreen.Close();
            profile.Deck = deckBefore;
            _campaign.Save();

            // Decks you don't own yet: a recipe pack and the outright price in the Recipe Book, a loaner on the versus
            // screen, and house packs in the shop once the Expert band is open.
            DebugAddDp(profile, 2000);
            _recipeBook.Open();
            _recipeBook.DebugSelect(bossDeck);
            int completeBefore = _recipeBook.DebugCompletePrice();
            int packCost = _recipeBook.DebugRecipePack();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("recipe-pack");
            Report($"a recipe pack for {bossDeck} costs {packCost} DP and lowers the missing cards' price ({completeBefore} -> {_recipeBook.DebugCompletePrice()} DP)",
                packCost > 0 && _recipeBook.DebugCompletePrice() < completeBefore);
            _recipeBook.Close();
            _campaignScreen.Close();

            // The Locker: the poster on your plate (Shop Bell from the start), the emblem instead, and locked posters
            // that can't be hung yet.
            _campaignScreen.Open();
            int earnedPosters = _campaignScreen.DebugOpenLocker();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("locker");
            bool emblemPicked = _campaignScreen.DebugPickPoster(Posters.Emblem) && Posters.Current(_campaign.Circuits, profile) == null;
            bool lockedRefused = Posters.IsUnlocked(Posters.All[Posters.All.Count - 1], _campaign.Circuits, profile) || !_campaignScreen.DebugPickPoster(Posters.All[Posters.All.Count - 1].Id);
            _campaignScreen.DebugPickPoster(Posters.All[0].Id);
            _campaignScreen.DebugCloseLocker();
            _campaignScreen.Close();
            Report($"the Locker shows {earnedPosters} earned poster(s) of {Posters.All.Count}; the emblem can be hung instead, a locked poster can't, "
                   + $"and {Posters.Current(_campaign.Circuits, profile)?.Title} is back on the plate",
                earnedPosters >= 1 && emblemPicked && lockedRefused && Posters.Current(_campaign.Circuits, profile)?.Id == Posters.All[0].Id);

            // Card unlocks: the bounty board, a set's poster in the Locker, the bond trade dialog (state put back after).
            yield return UnlocksCheck(Report, Shot);

            // The shop opens later years as you level (up to the next band's era), and sells house packs of the decks ahead.
            int band = _campaign.Circuits.BandEra(profile), nextEra = _campaign.Circuits.NextEra(profile), year = _campaign.Circuits.ShopYear(profile);
            Report($"at level {profile.Level} the shop opens sets up to {year} (band era {band}, next band {nextEra})", year > band && year <= nextEra);
            _shop.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            var early = _shop.DebugAvailable().Where(n => n.StartsWith("House Pack")).ToList();
            var houseDecks = _campaign.Circuits.HouseMatches(profile).Select(m => m.entry.Deck).Distinct().ToList();
            var houseCircuits = _campaign.Circuits.SelectionPackCircuits(profile).Select(c => _campaign.Circuits.All[c].Name).ToList();
            yield return Shot("shop-level");
            Report($"house packs cover the current and the next circuit ({string.Join(" and ", houseCircuits)}: {early.Count} packs for {houseDecks.Count} decks not beaten; {string.Join(", ", early.Take(3))}...)",
                early.Count >= 3 && early.Count <= houseDecks.Count && houseCircuits.Count == 2 && houseCircuits[0] == _campaign.Circuits.All[_campaign.Circuits.Current(profile)].Name);
            Report("the OCG import shelf waits for the Expert band", !_shop.DebugAvailable().Any(n => n.StartsWith("OCG Import")));
            _shop.Close();
            _campaignScreen.Close();

            // The Format Library: it opens after the Tier 0 Gauntlet; its shelf; a cup holds your deck to its own list and
            // pool whatever the mode; a cup match's versus. (The Gauntlet's wins come off again after, for the checks below.)
            var lib = _campaign.Library;
            int gauntletIndex = Enumerable.Range(0, _campaign.Circuits.All.Count).First(i => _campaign.Circuits.All[i].Name == Library.OpensAfter);
            var gauntletKeys = Enumerable.Range(0, _campaign.Circuits.All[gauntletIndex].Matches.Count).Select(m => Profile.MatchKey(gauntletIndex, m)).ToList();
            bool libShut = !profile.LibraryOpen && !lib.Open(profile, _campaign.Circuits);
            foreach (var k in gauntletKeys) profile.MatchWins[k] = 1;
            Report($"the Library opens after the {Library.OpensAfter} ({lib.Cups.Count} cups)", libShut && lib.Open(profile, _campaign.Circuits) && lib.Cups.Count == 12);
            _campaignScreen.DebugShowLibrary(-1);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("library-shelf");
            _campaignScreen.DebugShowLibrary(lib.Cups.Count - 1);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("library-cup");
            var goatRules = _campaign.CupRules(0);
            uint ashBlossom = 14558127;
            var lateDeck = _library.Load(profile.Deck).Clone();
            lateDeck.Main[lateDeck.Main.Count - 1] = ashBlossom;
            profile.Collection.Add(ashBlossom, Ygo.Engine.Data.Rarity.Common);
            bool lateInCup = _campaign.Problems(lateDeck, goatRules).Any(p => p.Message.Contains("not released yet"));
            bool lateInCircuit = _campaign.Problems(lateDeck, 0).Any(p => p.Message.Contains("not released yet"));
            Report($"the {lib.Cups[0].Name} holds your deck to {goatRules.List?.Name} and cards out by {goatRules.Pool}: Ash Blossom is out of its pool there "
                   + $"({lateInCup}), and only there outside era-limited play ({lateInCircuit})",
                goatRules.List != null && goatRules.Pool == lib.Cups[0].Pool && goatRules.Passes == BanPasses.Max && lateInCup && (profile.EraLimited || !lateInCircuit));
            var cupLoaner = _campaign.LoanerDecks().FirstOrDefault();
            var cupLoanerDeck = cupLoaner != null ? _campaign.Loaner(cupLoaner, goatRules) : null;
            Report($"a loaner at the {lib.Cups[0].Name} keeps to its list and pool ({cupLoaner}: {cupLoanerDeck?.Main.Count} cards)",
                cupLoanerDeck != null && _campaign.Problems(cupLoanerDeck, goatRules).All(p => !p.Message.Contains("not released yet") && !p.Message.Contains("Forbidden") && !p.Message.Contains("Limited")));
            // The Library Trader: a cup's Card Trader button opens the trader on its pool, whatever the mode. Ash Blossom is
            // missing from its search and refused; Sangan is offered, tagged with its place on the cup's list; Back returns
            // to the cup, and the shop's trader is the any-era one again.
            _campaignScreen.DebugShowLibrary(0);
            profile.Tokens += 3;
            const uint sangan = 26202165;
            _campaignScreen.DebugCupTrader(0);
            bool libTraderOpen = _trader.IsOpen && !_campaignScreen.IsOpen && _trader.Title == "LIBRARY TRADER";
            bool ashHidden = !_trader.DebugSearch("Ash Blossom").Any(c => c.Code == ashBlossom);
            var ashWhy = _trader.DebugBlocked(ashBlossom);
            var sanganShown = _trader.DebugSearch("Sangan").Any(c => c.Code == sangan);
            var sanganWhy = _trader.DebugBlocked(sangan);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("library-trader");
            _trader.DebugSearch(""); // the search box is the screen's: leave it empty for the trader checks below
            Report($"a cup's Card Trader button opens the Library Trader on the {lib.Cups[0].Name}'s pool: Ash Blossom isn't listed ({ashHidden}) and is refused ({ashWhy}); Sangan can be traded ({sanganWhy ?? "allowed"})",
                libTraderOpen && ashHidden && ashWhy != null && ashWhy.Contains("this cup's cards end") && sanganShown && sanganWhy == null);
            _trader.Close();
            bool backToCup = _campaignScreen.IsOpen && _campaignScreen.LibraryCup == 0 && !_trader.IsOpen;
            bool traderEra = profile.EraLimited;
            profile.EraLimited = false;
            _trader.Open();
            var shopAsh = _trader.DebugBlocked(ashBlossom);
            Report($"Back returns to the cup ({backToCup}); the shop's trader is the any-era one again ({_trader.Title}: Ash Blossom {shopAsh ?? "allowed"})",
                backToCup && _trader.Title == "CARD TRADER" && shopAsh == null);
            profile.EraLimited = traderEra;
            _trader.Close();
            profile.Tokens -= 3;

            // The Library's cosmetics, with the Goat Cup cleared by your deck: its era mat bought from the Locker's Mats tab
            // goes on your side; its binder engraved with your deck (silver: not stamped); a second engraving with the same
            // deck is refused. Everything comes off again after.
            var goatId = Library.Id(lib.Cups[0]);
            var goatMat = Cosmetics.MatId(lib.Cups[0]);
            string matBefore = profile.Mat;
            var cosmeticsDp = SnapshotDp(profile);
            for (int m = 0; m < lib.Cups[0].Matches.Count; m++) profile.LibraryWins[lib.Key(0, m)] = 1;
            profile.LibraryDecks[goatId] = new HashSet<string> { profile.Deck };
            DebugSetDp(profile, Cosmetics.MatPrice + Cosmetics.EngravePrice + 500);
            _campaignScreen.DebugShowLibrary(0);
            _campaignScreen.DebugLockerMats(true);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("locker-mats-for-sale");
            var matNote = _campaignScreen.DebugMatTile(0);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("locker-mats");
            Report($"a cleared cup's era mat is bought from the Locker and goes on your side ({StripTags(matNote)})",
                profile.Mats.Contains(goatMat) && profile.Mat == goatMat && profile.Dp == Cosmetics.EngravePrice + 500 && YourMat()?.id == goatMat);
            _campaignScreen.DebugLockerMats(false); // the Locker opens on its Posters tab again
            _campaignScreen.DebugCloseLocker();
            var engraveNote = _campaignScreen.DebugEngrave(0);
            var again = _campaignScreen.DebugEngrave(0);
            _campaignScreen.DebugShowLibrary(0);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("library-engraved");
            Report($"its binder is engraved with your deck ({StripTags(engraveNote)}), and not twice with the same deck ({StripTags(again)})",
                profile.Engravings.TryGetValue(goatId, out var plate) && plate == DeckTitle(profile.Deck) && profile.Dp == 500 && again.Contains("it's engraved"));
            foreach (var k in Enumerable.Range(0, lib.Cups[0].Matches.Count).Select(m => lib.Key(0, m))) profile.LibraryWins.Remove(k);
            profile.LibraryDecks.Remove(goatId);
            profile.Mats.Remove(goatMat);
            profile.Engravings.Remove(goatId);
            profile.Mat = matBefore;
            RestoreDp(profile, cosmeticsDp); // DP and the mats' and plates' ledger lines as they were

            // Library Locals: the button, the entry, the bracket, its versus, a loss and Skip to end, a title's Prize Packs.
            yield return BracketCheck(Report, Shot);

            // The "Every Era" final: its plaque waits for every cup; with all 12 cleared it opens, round 1 can be played and
            // round 2 can't yet; round 1's Duel opens a versus with a deck of yours (never a loaner), and one that isn't
            // legal in 2005 can't start it. The cups' wins come off again after.
            bool finalShut = !lib.FinalOpen(profile);
            _campaignScreen.DebugShowLibrary(lib.Cups.Count);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("library-final-locked");
            var cupWins = Enumerable.Range(0, lib.Cups.Count)
                .SelectMany(c => Enumerable.Range(0, lib.Cups[c].Matches.Count).Where(m => !lib.Cups[c].Matches[m].Champion).Select(m => lib.Key(c, m)))
                .Where(k => !profile.LibraryWins.ContainsKey(k)).ToList();
            foreach (var k in cupWins) profile.LibraryWins[k] = 1;
            _campaignScreen.DebugShowLibrary(lib.Cups.Count);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("library-final");
            Report($"the {lib.Final.Name} final opens once every cup is cleared, on its own panel; round 1 is next and round 2 waits for it",
                finalShut && lib.FinalOpen(profile) && _campaignScreen.LibraryCup == lib.Cups.Count && lib.CanPlayRound(profile, 0) && !lib.CanPlayRound(profile, 1));
            _campaignScreen.DebugRoundDuel(0);
            var roundShown = Time.realtimeSinceStartup + 6;
            while (!_scenes.IsOpen && Time.realtimeSinceStartup < roundShown) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("final-versus");
            var roundDeck = _versusDeck ?? "";
            bool roundIllegal = OwnDeckProblems(roundDeck, _campaign.RoundRules(0)).Count > 0;
            bool refused = !roundIllegal;
            if (roundIllegal && _scenes.Buttons.TryGetValue("Duel!", out var roundStart))
            {
                roundStart();
                yield return new WaitForSecondsRealtime(0.3f);
                refused = _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel!") && _runner?.IsWaiting != true;
                yield return Shot("final-versus-refused");
            }
            Report($"round 1's Duel closes the campaign screen for its versus, which offers a deck of yours ({VersusDeckName(roundDeck)}{(roundIllegal ? ", not legal in 2005: Duel! refuses it" : ", legal in 2005")})",
                _scenes.IsOpen && !_campaignScreen.IsOpen && !_campaign.IsLoanerDeck(roundDeck) && refused);
            _scenes.Close();
            _campaignScreen.Close();
            foreach (var k in cupWins) profile.LibraryWins.Remove(k);
            _campaignScreen.DebugShowLibrary(0);
            var cupRefusal = _campaignScreen.DebugTryCupDuel(0, 0);
            var cupShown = Time.realtimeSinceStartup + 6;
            while (!_scenes.IsOpen && Time.realtimeSinceStartup < cupShown) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("library-versus");
            Report($"a cup match's Duel button opens its versus screen{(cupRefusal != null ? $" (refused: {cupRefusal})" : "")}", cupRefusal == null && _scenes.IsOpen);
            _scenes.Close();
            _campaignScreen.Close();
            foreach (var k in gauntletKeys) profile.MatchWins.Remove(k);
            profile.LibraryOpen = false;

            // Ban Passes: a Forbidden card you own is legal with a pass; a card that wins on its own never is. (Under a list,
            // as in era-limited play.)
            _campaign.DebugListsApply = true;
            int listed = Enumerable.Range(0, _campaign.Circuits.All.Count).First(i => _campaign.ListFor(i) is { } l && !l.IsUnrestricted);
            var passList = _campaign.ListFor(listed)!;
            uint potOfGreed = 55144522, lastTurn = 28566710;
            var passDeck = _library.Load(profile.Deck).Clone();
            passDeck.Name = "Ban Pass check";
            passDeck.Campaign = true;
            passDeck.Main.RemoveRange(passDeck.Main.Count - 2, 2);
            passDeck.Main.Add(potOfGreed);
            passDeck.Main.Add(lastTurn);
            profile.Collection.Add(potOfGreed, Ygo.Engine.Data.Rarity.Common);
            profile.Collection.Add(lastTurn, Ygo.Engine.Data.Rarity.Common);
            bool Flags(uint code) => _campaign.Problems(passDeck, listed).Any(p => p.Message.Contains(_cardPack!.Get(code)?.Name ?? "?"));
            bool flaggedBefore = Flags(potOfGreed);
            BanPasses.Set(passDeck, new[] { potOfGreed, lastTurn });
            Report($"a Ban Pass makes Pot of Greed legal under {passList.Name} ({flaggedBefore} -> {Flags(potOfGreed)}), Last Turn can't be passed ({Flags(lastTurn)}), {_campaign.PassAllowance} passes allowed",
                flaggedBefore && !Flags(potOfGreed) && Flags(lastTurn) && _campaign.PassAllowance == BanPasses.Starting);
            _library.Save(passDeck);
            _editor.Open(passDeck.Name, profile.Collection, passList.Name);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("editor-ban-pass");
            _editor.Close();
            // Outside era-limited play nobody answers to the list: the same deck needs no pass.
            _campaign.DebugListsApply = null;
            BanPasses.Set(passDeck, Array.Empty<uint>());
            Report($"outside era-limited play your deck ignores {passList.Name}: Pot of Greed and Last Turn are allowed ({!Flags(potOfGreed) && !Flags(lastTurn)})",
                profile.EraLimited || (!Flags(potOfGreed) && !Flags(lastTurn) && _campaign.ListFor(listed) == null));
            _library.Delete(passDeck.Name);
            _campaignScreen.Close();

            // Who answers to a list: a loaner only in era-limited play; a partner's own recipe and a CPU never.
            int lateList = Enumerable.Range(0, _campaign.Circuits.All.Count).Last(i => _campaign.CircuitList(i) != null);
            var tear = _library.Load("tearlaments");
            bool wasEra = profile.EraLimited;
            profile.EraLimited = false;
            var openLoaner = _campaign.Loaner(Loaners.NameFor("tearlaments"), lateList);
            var openPartner = _campaign.PartnerPlays("tearlaments", lateList);
            profile.EraLimited = true;
            var eraLoaner = _campaign.Loaner(Loaners.NameFor("tearlaments"), lateList);
            var eraPartner = _campaign.PartnerPlays("tearlaments", lateList);
            int eraChanges = _campaign.PartnerChanges("tearlaments", lateList).Count;
            profile.EraLimited = wasEra;
            bool Same(Deck a, Deck b) => a.Main.OrderBy(c => c).SequenceEqual(b.Main.OrderBy(c => c)) && a.Extra.OrderBy(c => c).SequenceEqual(b.Extra.OrderBy(c => c));
            Report($"at {_campaign.Circuits.All[lateList].Name}, the Tearlaments loaner and a partner's Tearlaments play the recipe as built outside "
                   + $"era-limited play ({Same(openLoaner, tear)}, {Same(openPartner, tear)}); in it the loaner follows {_campaign.CircuitList(lateList)!.Name} "
                   + $"and the partner's own deck still plays as built ({Same(eraPartner, tear)}, {eraChanges} changes)",
                Same(openLoaner, tear) && Same(openPartner, tear) && !Same(eraLoaner, tear) && Same(eraPartner, tear) && eraChanges == 0);

            // Editing a partner's deck: a deck of yours with their cards on hand, offered as theirs, answering to the list.
            var vera = _roster.First(p => p.Id == "vera");
            var ownVera = _campaign.PartnerDeck(vera);
            var verasEdit = _campaign.EditPartnerDeck(vera);
            bool editOk = verasEdit != null && _library.IsCampaign(verasEdit) && _campaign.PartnerEditOf(verasEdit) == "vera"
                          && _campaign.PartnerDeck(vera) == verasEdit && _campaign.PartnerDeckChoices(vera).Contains(ownVera)
                          && !_campaign.IsLoanerDeck(verasEdit) && Playbooks.RecipeOf(_library.Load(verasEdit)) == ownVera;
            var editCards = verasEdit != null ? _campaign.CollectionFor(_library.Load(verasEdit)) : new Collection();
            bool theirCards = _library.Load(ownVera).Main.All(c => editCards.Count(Banlist.BaseCode(c, _cardPack!)) > 0);
            _campaign.UsePartnersOwnDeck(vera);
            bool reset = _campaign.PartnerDeck(vera) == ownVera;
            if (verasEdit != null) _library.Delete(verasEdit);
            Report($"editing Vera's deck saves {verasEdit} (yours, plays by {ownVera}'s playbook, her cards on hand: {theirCards}); Their own deck brings {_campaign.PartnerDeck(vera)} back",
                editOk && theirCards && reset);

            // The toss tip at a circuit match, for a deck measured there (campaign/toss-advice.txt): your side only.
            int houston = _campaign.Circuits.All.ToList().FindIndex(c => c.Name.Contains("Houston 2026"));
            var versusBefore = _versusDeck;
            _versusDeck = Loaners.NameFor("tearlaments");
            var tossTip = houston >= 0 ? MeasuredToss(Profile.MatchKey(houston, 0)) : null;
            _versusDeck = "mekk-knight-invoked";
            var noTip = houston >= 0 ? MeasuredToss(Profile.MatchKey(houston, 0)) : null;
            _versusDeck = versusBefore;
            Report($"at Houston 2026 match 1 the toss tip for the Tearlaments loaner says go second ({tossTip?.hint}); a deck not measured there gets none",
                tossTip is { first: false } && tossTip.Value.hint.Contains("going second") && noTip == null);

            // A bot with a loaner or a copy of a deck plays by its recipe's playbook (Kit with a loaner, the QA stand-in).
            var lent = new Deck { Name = "Ryzeal / Mitsurugi Ryzeal (loaner)" };
            lent.Notes["loaner"] = "ryzeal";
            Report("a loaner and a copied deck play by their recipe's playbook (Ryzeal loaner, Mekk-Knight Invoked Evolved (new))",
                Playbooks.ForDeck(lent) != null && Playbooks.ForDeck("Mekk-Knight Invoked Evolved (new)") != null);

            // A custom loaner: the recipe as a deck of yours to edit, its cards borrowed, still a loaner.
            var custom = _campaign.CustomizeLoaner(bossDeck, DeckTitle(bossDeck));
            var customDeck = _library.Load(custom);
            Report($"\"Customize as loaner\" saves {custom}: it can use the recipe's cards without owning them, and counts as a loaner",
                _campaign.IsLoanerDeck(custom) && _campaign.Problems(customDeck, lastBeginner).All(p => !p.Message.Contains("own")));
            _library.Delete(custom);

            var loanerMatch = (circuit: lastBeginner, match: lastMatch);
            _versusDeck = null;
            ShowVersus(loanerMatch.circuit, loanerMatch.match);
            yield return new WaitForSecondsRealtime(0.3f);
            var loanerKey = _scenes.Buttons.Keys.FirstOrDefault(k => k.StartsWith(">@780"));
            for (int i = 0; i < 40 && loanerKey != null && !(Loaners.IsLoaner(_versusDeck ?? "")); i++) _scenes.Buttons[loanerKey]();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("versus-loaner");
            Report($"the versus screen's deck arrows reach a loaner ({_versusDeck})", Loaners.IsLoaner(_versusDeck ?? ""));
            _scenes.Buttons["Duel!"]();
            yield return new WaitForSecondsRealtime(0.5f);
            Report($"Duel! with a loaner plays the borrowed deck ({_recording?.DeckNames[0]}) and keeps your own deck ({profile.Deck})",
                _recording?.DeckNames[0].EndsWith("(loaner)") == true && profile.Deck == deckBefore);
            // The check drops this duel unplayed: its live record goes with it (a player's would be a loss).
            _campaign.EndLiveDuel();
            _campaign.Save();
            _campaignMatch = null;
            _versusDeck = null;
            _paused = true;
            // Pretend the Expert band is open, look at the shop, then put the save back.
            var savedWins = new Dictionary<string, int>(profile.MatchWins);
            int firstLate = Enumerable.Range(0, _campaign.Circuits.All.Count).First(_campaign.Circuits.IsLateBand);
            for (int ci = 0; ci < firstLate; ci++)
                for (int mi = 0; mi < _campaign.Circuits.All[ci].Matches.Count; mi++) profile.MatchWins[Profile.MatchKey(ci, mi)] = 1;
            _shop.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            var selection = _shop.DebugAvailable().Where(n => n.StartsWith("House Pack")).ToList();
            if (selection.Count > 0) _shop.DebugSelect(selection[0]);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("house-packs");
            Report($"with the Expert band open the shop sells {selection.Count} house packs ({string.Join(", ", selection.Take(4))})", selection.Count >= 4);
            // The OCG import shelf: themed packs of cards the TCG never printed, at a big price; not in era-limited play.
            var imports = _shop.DebugAvailable().Where(n => n.StartsWith("OCG Import")).ToList();
            if (imports.Count > 0) _shop.DebugSelect(imports[0]);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("ocg-imports");
            Report($"and {imports.Count} OCG import packs ({string.Join(", ", imports.Take(4))}...) at {ImportShelf.Price} DP",
                imports.Count >= 5 && _campaign.Imports.Packs.All(p => p.Price == ImportShelf.Price));
            int dpBeforeImport = profile.Dp;
            var pulled = imports.Count > 0 ? _shop.DebugBuyPack(imports[0]) : new List<uint>();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("ocg-import-pack");
            Report($"an import pack holds {pulled.Count} OCG-only cards, now in your collection ({string.Join(", ", pulled.Take(3).Select(c => _cardPack!.Get(c)?.Name))}...)",
                pulled.Count == PackCatalog.PackSize && pulled.All(_campaign.Imports.IsOcgOnly) && pulled.All(profile.Collection.Owns) && profile.Dp >= dpBeforeImport);
            bool limitedBefore = profile.EraLimited;
            profile.EraLimited = true;
            Report("era-limited play has no import shelf", !_shop.DebugAvailable().Any(n => n.StartsWith("OCG Import")));
            profile.EraLimited = limitedBefore;
            _shop.Close();
            _campaignScreen.Close(); // closing the shop goes back to the campaign screen, which would pause the duels after this
            profile.MatchWins.Clear();
            foreach (var (k, v) in savedWins) profile.MatchWins[k] = v;
            DebugSetDp(profile, profile.Dp - Math.Min(profile.Dp, 2000 - packCost));
            _campaign.Save();

            // Gus's guided first duel: offered once after the opening scene (Skip goes to the circuits), first in the
            // Lessons list, played through its hints with the board pointing at each step, passed, then on to the circuits.
            OfferFirstDuel();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("first-duel-offer");
            Report("a new campaign offers Gus's first duel", _scenes.IsOpen && _scenes.Buttons.ContainsKey("Practice duel") && _scenes.Buttons.ContainsKey("Skip: I know how to play"));
            if (_scenes.Buttons.TryGetValue("Skip: I know how to play", out var skipFirst)) skipFirst();
            yield return new WaitForSecondsRealtime(0.3f);
            Report("Skip goes to the circuits", _campaignScreen.IsOpen && !_scenes.IsOpen);
            _campaignScreen.Close();
            OfferFirstDuel();
            Report("the first duel is offered only once", _campaignScreen.IsOpen && !_scenes.IsOpen);
            _campaignScreen.Close();
            ShowLessons();
            yield return new WaitForSecondsRealtime(0.4f);
            Report("the lessons list starts with the first duel", _scenes.IsOpen && _scenes.Buttons.ContainsKey("First duel"));
            _scenes.Buttons["First duel"]();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("first-duel-intro");
            Report($"Gus explains the board first ({_storyScreen.DebugText})", _storyScreen.IsOpen);
            _storyScreen.DebugFinish();
            yield return new WaitForSecondsRealtime(0.5f);
            Report($"the first duel starts both players at 2000 LP ({_view.LifePoints(0)} / {_view.LifePoints(1)})", _view.LifePoints(0) == 2000 && _view.LifePoints(1) == 2000);
            yield return PlayFirstDuel(Shot, Report);
            yield return Shot("first-duel-passed");
            Report($"following the hints wins the first duel ({_lessonJudge?.Hint})",
                _lessonJudge?.Outcome == LessonOutcome.Passed && profile.Seen.Contains(ChainLessons.FirstDuel.SeenKey) && _scenes.Buttons.ContainsKey("On to the circuit"));
            Report("the pointer is gone once the lesson is over", _view.PointingAt == "");
            if (_scenes.Buttons.TryGetValue("On to the circuit", out var onward)) onward();
            yield return new WaitForSecondsRealtime(0.3f);
            Report("On to the circuit opens the circuits", _campaignScreen.IsOpen && !_scenes.IsOpen);
            _campaignScreen.Close();

            // Gus's chain lessons: the list, Gus's intro, lesson 1 played right (it passes and is saved), then lesson 2
            // left on Auto (it fails and says why).
            ShowLessons();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("lessons");
            Report("the lessons list offers Gus's three lessons", _scenes.IsOpen && _scenes.Buttons.ContainsKey("Lesson 1") && _scenes.Buttons.ContainsKey("Lesson 3"));
            _scenes.Buttons["Lesson 1"]();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("lesson-intro");
            Report($"Gus explains the lesson first ({_storyScreen.DebugText})", _storyScreen.IsOpen);
            _storyScreen.DebugFinish();
            yield return PlayChainLesson(new[] { ChainLessons.ReinforcementOfTheArmy }, new[] { ChainLessons.CalledByTheGrave },
                new[] { ChainLessons.AshBlossom, ChainLessons.Gearfried }, "lesson-chain", Shot);
            yield return Shot("lesson-passed");
            Report($"answering Ash with Called by the Grave passes lesson 1 ({_lessonJudge?.Hint})",
                _lessonJudge?.Outcome == LessonOutcome.Passed && _scenes.Buttons.ContainsKey("Next lesson") && profile.Seen.Contains(ChainLessons.All[0].SeenKey));
            _scenes.Buttons["Next lesson"]();
            yield return new WaitForSecondsRealtime(0.3f);
            _storyScreen.DebugFinish();
            yield return PlayChainLesson(Array.Empty<uint>(), new[] { ChainLessons.JarOfGreed }, Array.Empty<uint>(), "lesson-banner", Shot);
            yield return Shot("lesson-failed");
            Report($"waiting on Auto fails lesson 2 and says to turn Chain ON ({_lessonJudge?.Hint})",
                _lessonJudge?.Outcome == LessonOutcome.Failed && _lessonJudge.Hint.Contains("ON") && _scenes.Buttons.ContainsKey("Try again"));
            _scenes.Close();
            _chainLesson = null;

            // Story: the opening scene, the one a cleared circuit triggers, and a partner's first tag duel.
            bool storyDone = false;
            PlayStory(new[] { "start" }, "", () => storyDone = true);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("story-start");
            Report($"the opening story scene plays ({_storyScreen.DebugText})", _storyScreen.IsOpen && _storyScreen.LinesShown > 0);
            _storyScreen.DebugFinish();
            Report("finishing it continues the campaign and marks it seen", storyDone && profile.Seen.Contains("welcome") && _story.Pending(profile, new[] { "start" }).Count == 0);
            PlayStory(new[] { "clear:Regional Qualifier" }, "", () => { });
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("story-band");
            Report($"clearing the Beginner band tells you about ban lists ({_storyScreen.DebugText})", _storyScreen.IsOpen && profile.Seen.Contains("novice"));
            _storyScreen.DebugFinish();
            var storyPartner = _campaign.Circuits.Partners(profile).FirstOrDefault()?.Id ?? "vera";
            PlayStory(new[] { "partner", "partner:" + storyPartner }, storyPartner, () => { });
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("story-partner");
            Report($"a new partner introduces themselves ({_storyScreen.DebugText})", _storyScreen.IsOpen && _storyScreen.DebugText.Length > 2);
            _storyScreen.DebugFinish();

            // Era ban lists (era-limited play): the first circuit with one flags the starter deck's banned cards, and Edit
            // deck opens under it.
            _campaign.DebugListsApply = true;
            var era = Enumerable.Range(0, _campaign.Circuits.All.Count).First(i => _campaign.ListFor(i) != null);
            var eraList = _campaign.ListFor(era)!;
            var issues = _campaign.Problems(_library.Load(profile.Deck), era);
            Report($"circuit {era + 1} plays under {eraList.Name}; the starter deck is flagged ({issues.FirstOrDefault()?.Message})",
                issues.Count > 0 && _campaign.Problems(_library.Load(profile.Deck), 0).Count == 0);
            _campaignScreen.DebugShowCircuit(era);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("era-circuit");
            _campaignScreen.Close();
            _editor.Open(profile.Deck, profile.Collection, eraList.Name);
            Report($"Edit deck opens under the circuit's list ({_editor.BanlistName})", _editor.BanlistName == eraList.Name);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("era-editor");
            int left = _editor.DebugAutoFix(out var fixStatus);
            Report($"Auto-fix makes the deck legal under {eraList.Name} from owned cards ({left} problems left): {fixStatus}", left == 0);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("era-autofix");
            left = _editor.DebugRecipe(out var recipeStatus);
            Report($"Recipe builds a legal deck from owned cards ({left} problems left): {recipeStatus}", left == 0);
            // Next recipe (one you own less of), so the wishlist has cards on it; then the shop in wishlist order.
            _editor.DebugRecipe(out _);
            if (profile.Wanted().Count == 0)
            {
                // Nothing else is close to owned yet: wish for the zombie starter's cards instead.
                foreach (var w in AutoDeck.Wants(_library.Load("zombies"), eraList, _cardPack!)) profile.Wishlist[w.Key] = w.Value;
                profile.WishlistFor = "zombies";
            }
            _editor.Close();
            _campaign.DebugListsApply = null;
            _campaignScreen.Close();
            int sets = _shop.DebugWishlist();
            Report($"the wishlist ({profile.Wanted().Count} cards for {profile.WishlistFor}) ranks {sets} shop sets", profile.Wanted().Count > 0 && sets > 0);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("shop-wishlist");
            Report($"levels and tokens: level {profile.Level}, {profile.Exp} EXP, {profile.Tokens} tokens (the circuit clear above levelled up)",
                profile.Level > 1 && news.Any(u => u.StartsWith("Level")));

            // The Card Trader: a token buys a wishlist card; a card from after the shop's era is refused.
            profile.Tokens += 2;
            int tokens = profile.Tokens, before = profile.Collection.Total;
            var traded = _trader.DebugTradeFirst();
            Report($"the Card Trader trades a token for {traded}", traded != null && profile.Tokens == tokens - 1 && profile.Collection.Total == before + 1);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("trader");
            // DP buys a token; Ban Passes are earned, never sold.
            DebugAddDp(profile, 10000);
            int tokensNow = profile.Tokens, allowance = _campaign.PassAllowance;
            bool limitedNow = profile.EraLimited;
            profile.EraLimited = true;
            var boughtToken = _trader.DebugBuy();
            profile.EraLimited = limitedNow;
            Report($"DP buys a Card Token ({StripTags(boughtToken)}); Ban Passes aren't sold, even in era-limited play ({allowance} -> {_campaign.PassAllowance})",
                profile.Tokens == tokensNow + 1 && _campaign.PassAllowance == allowance);
            // Any era normally; the historic challenge (era-limited) refuses a card from after the shop's era.
            var ash = _trader.DebugSearch("Ash Blossom").FirstOrDefault(c => c.Name == "Ash Blossom & Joyous Spring");
            bool wasLimited = profile.EraLimited;
            profile.EraLimited = false;
            var free = ash == null ? "not found" : CardTrader.Blocked(ash.Code, profile, _campaign.Circuits.ShopYear(profile), CardFoil.Printings, _cardPack!);
            profile.EraLimited = true;
            var why = ash == null ? null : CardTrader.Blocked(ash.Code, profile, _campaign.Circuits.ShopYear(profile), CardFoil.Printings, _cardPack!);
            profile.EraLimited = wasLimited;
            Report($"a 2017 card can be traded for from the start ({free ?? "allowed"}), and the historic challenge refuses it ({why})",
                free == null && why != null && why.Contains("first printed"));
            // The OCG-only filter: every card the TCG never printed, each with its OCG badge; not offered in era-limited play.
            var ocgCards = _trader.DebugOcgOnly(true);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("trader-ocg");
            Report($"the Card Trader's OCG-only filter lists {ocgCards.Count} cards, all OCG-only", ocgCards.Count == _campaign.Imports.Cards.Count && ocgCards.All(c => _campaign.Imports.IsOcgOnly(c.Code)));
            _trader.DebugOcgOnly(false);
            profile.EraLimited = true;
            _trader.Open();
            bool hidden = !_trader.OcgFilterShown;
            profile.EraLimited = wasLimited;
            _trader.Open();
            Report("and era-limited play doesn't offer it", hidden && _trader.OcgFilterShown);
            _trader.Close();
            _shop.Close();
            _campaignScreen.Close();

            // A Tag Cup: open after its circuit, partner picked from duelists you've beaten, a tag match played with
            // random clicks for your turns, and the results.
            _campaignScreen.DebugShowTagCup(0);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("tag-cup");
            var partnerChoices = _campaign.Circuits.Partners(profile);
            Report($"Tag Cup \"{_campaign.Circuits.TagCups[0].Name}\" is open with {partnerChoices.Count} partners ({string.Join(", ", partnerChoices.Select(p => p.Name))})",
                _campaign.Circuits.IsTagCupOpen(0, profile) && partnerChoices.Count > 0);
            _campaignScreen.Close();
            ShowTagVersus(0, 0, partnerChoices[0].Id);
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("tag-versus");
            _scenes.Buttons["Duel!"]();
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            var tagClicker = StartCoroutine(AutoClick());
            var tagDeadline = Time.realtimeSinceStartup + 240;
            while (!_announcedResult && Time.realtimeSinceStartup < tagDeadline) yield return null;
            StopCoroutine(tagClicker);
            var tagShown = Time.realtimeSinceStartup + 6;
            while (!_scenes.IsOpen && Time.realtimeSinceStartup < tagShown) yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("tag-results");
            Report($"the tag match finished (winner team {_board.Winner}) and the results show: {_scenes.LastResults?.Circuit}",
                _announcedResult && _tag != null && _scenes.IsOpen && (_scenes.LastResults?.Circuit.Contains("Tag match") ?? false));
            if (_scenes.Buttons.TryGetValue("Continue", out var afterTag)) afterTag();
            yield return new WaitForSecondsRealtime(0.4f);
            _campaignScreen.Close();

            // Scout from the campaign screen, to prepare a deck: each card to build with says how many you own.
            var lateDecks = new[] { ("Sumire Ishigaki", "mitsurugi"), ("Corin Vasse", "branded-despia") };
            var prepared = Scouts(lateDecks);
            _scenes.ShowScoutReport(prepared, () => _campaignScreen.Open());
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("campaign-scout");
            Report($"the campaign screen's Scout shows what to build with and what you own ({string.Join(", ", prepared[0].Build.Take(3))})",
                _scenes.ScoutingOpen && prepared.All(p => p.Build.Count >= 3 && p.Build.All(b => b.Contains("(you have") || b.Contains("(none yet)"))));
            if (_scenes.Buttons.TryGetValue("Close scouting", out var closePrepared)) closePrepared();
            yield return new WaitForSecondsRealtime(0.3f);
            Report("closing it goes back to the campaign", _campaignScreen.IsOpen && !_scenes.ScoutingOpen);
            _campaignScreen.Close();

            // Player tools for hard matches: the Tag Cup versus screen's deck arrows reach a loaner (played just for the
            // duel), Kit can bring one, Scout shows a report on each opposing deck, and three losses in a row there open
            // the "Stuck?" advice by itself.
            var tagPartner = partnerChoices[0].Id;
            ShowTagVersus(0, 0, tagPartner);
            yield return new WaitForSecondsRealtime(0.4f);
            var tagArrow = _scenes.Buttons.Keys.FirstOrDefault(k => k.StartsWith(">@780"));
            for (int i = 0; i < 60 && tagArrow != null && !Loaners.IsLoaner(_versusDeck ?? ""); i++) _scenes.Buttons[tagArrow]();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("tag-versus-loaner");
            Report($"the Tag Cup versus screen's deck arrows reach a loaner ({_versusDeck})", tagArrow != null && Loaners.IsLoaner(_versusDeck ?? ""));
            bool scoutButton = _scenes.Buttons.TryGetValue("Scout", out var scoutTag);
            scoutTag?.Invoke();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("tag-scouting");
            var (tagLead, tagSecond) = _campaign.Circuits.TagCups[0].Matches[0];
            Report($"Scout shows a report on both their decks ({string.Join(" | ", Scouts((tagLead.Character.Name, tagLead.Deck), (tagSecond.Character.Name, tagSecond.Deck)).Select(r => $"{r.Deck}: {string.Join(", ", r.Keys.Take(2))}"))})",
                scoutButton && _scenes.ScoutingOpen);
            if (_scenes.Buttons.TryGetValue("Close scouting", out var closeScout)) closeScout();
            string deckKept = profile.Deck;
            if (_scenes.Buttons.TryGetValue("Duel!", out var tagLoanerDuel)) tagLoanerDuel();
            yield return new WaitForSecondsRealtime(1.2f);
            Report($"Duel! with a loaner in a Tag Cup plays the borrowed deck ({_tag?.Decks[0]}) and keeps your own ({profile.Deck})",
                _tag != null && Loaners.IsLoaner(_tag.Decks[0]) && profile.Deck == deckKept && _recording?.DeckNames[0].EndsWith("(loaner)") == true);
            ConcedeDuel();
            var concededTag = Time.realtimeSinceStartup + 8;
            while (!(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue")) && Time.realtimeSinceStartup < concededTag) yield return null;
            if (_scenes.Buttons.TryGetValue("Continue", out var afterLoanerTag)) afterLoanerTag();
            yield return new WaitForSecondsRealtime(0.4f);
            _campaignScreen.Close();
            var kitChoices = _campaign.ShopRegular == null ? new List<string>() : _campaign.PartnerDeckChoices(_campaign.ShopRegular);
            Report($"Kit can bring a loaner ({kitChoices.Count(Loaners.IsLoaner)} recipes among {kitChoices.Count} decks)", kitChoices.Any(Loaners.IsLoaner));
            _versusDeck = profile.Deck;
            profile.LossStreaks[Circuits.TagKey(0, 1)] = StuckAfter;
            ShowTagVersus(0, 1, tagPartner);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("tag-stuck");
            var stuckMatch = _campaign.Circuits.TagCups[0].Matches[1];
            var stuckTips = Stuck(Circuits.TagKey(0, 1), true, new[] { stuckMatch.lead.Character.Id, stuckMatch.partner.Character.Id }, stuckMatch.lead.Deck) ?? new List<string>();
            Report($"after {StuckAfter} losses in a row the versus screen opens the Stuck? advice ({string.Join(" / ", stuckTips.Skip(1).Take(2))})",
                _scenes.ScoutingOpen && stuckTips.Count >= 3);
            profile.LossStreaks.Remove(Circuits.TagKey(0, 1));
            if (_scenes.Buttons.TryGetValue("Close scouting", out var closeStuck)) closeStuck();
            if (_scenes.Buttons.TryGetValue("Back", out var stuckBack)) stuckBack();
            yield return new WaitForSecondsRealtime(0.3f);
            _campaignScreen.Close();

            // Master Tag Cup match 3 as the cup opens (after Master League: Graveyard Kings): its decks come from Houston,
            // so the advice has to name a team you already have (measured: the Tearlaments loaner, 65-75%).
            var winsBeforeMaster = new Dictionary<string, int>(profile.MatchWins);
            var beatenBeforeMaster = new Dictionary<string, int>(profile.Wins);
            int masterCup = _campaign.Circuits.TagCups.ToList().FindIndex(t => t.Name == "Master Tag Cup");
            if (masterCup >= 0)
            {
                var cupM = _campaign.Circuits.TagCups[masterCup];
                for (int ci = 0; ci <= cupM.After; ci++)
                    for (int mi = 0; mi < _campaign.Circuits.All[ci].Matches.Count; mi++)
                    {
                        profile.MatchWins[Profile.MatchKey(ci, mi)] = 1;
                        var beaten = _campaign.Circuits.All[ci].Matches[mi].Character.Id;
                        profile.Wins[beaten] = Math.Max(1, profile.WinsAgainst(beaten));
                    }
                var (m3Lead, m3Partner) = cupM.Matches[2];
                var m3Key = Circuits.TagKey(masterCup, 2);
                profile.LossStreaks[m3Key] = StuckAfter;
                var m3Ids = new[] { m3Lead.Character.Id, m3Partner.Character.Id };
                var m3Tips = Stuck(m3Key, true, m3Ids, m3Lead.Deck, m3Partner.Deck) ?? new List<string>();
                Report($"stuck at the Master Tag Cup's match 3 as it opens, the advice names a team you have ({m3Tips.Skip(1).FirstOrDefault()})",
                    // (Graveyard Kings brings the Tearlaments loaner; Kelda Amin's Ishizu Tearlaments waits for the Gauntlet.)
                    m3Tips.Skip(1).FirstOrDefault()?.Contains("Tearlaments loaner") == true && !m3Tips[1].Contains("Ishizu"));

                // The Stuck? panel's purchase: "Make it yours" arms on the first click and buys on the second: the
                // loaner's very list saved as your campaign deck (full DP), playing by its recipe's playbook.
                var m3Pick = MeasuredPicks(m3Key, true, m3Ids).FirstOrDefault();
                if (m3Pick?.Partner != null && _campaign.QuoteOwn(m3Pick.Deck, cupM.After) is { } ownQuote)
                {
                    string ownDeckBefore = profile.Deck;
                    var ownDpBefore = SnapshotDp(profile);
                    DebugAtLeastDp(profile, ownQuote.Price + 1000);
                    int ownDpOffered = profile.Dp;
                    _versusDeck = profile.Deck;
                    ShowTagVersus(masterCup, 2, m3Pick.Partner.Id);
                    yield return new WaitForSecondsRealtime(0.5f);
                    bool ownOffered = _scenes.ScoutingOpen && _scenes.Buttons.TryGetValue("Own stuck deck", out var ownDeck);
                    yield return Shot("stuck-own-offer");
                    Report($"the Stuck? advice offers to own its loaner ({DeckTitle(ownQuote.Recipe)}: {ownQuote.Cards} cards, {ownQuote.Price:N0} DP, {ownQuote.StandIns} stand-ins)", ownOffered);
                    if (ownOffered && _scenes.Buttons.TryGetValue("Own stuck deck", out ownDeck))
                    {
                        ownDeck();
                        yield return new WaitForSecondsRealtime(CampaignScenes.ConfirmDelay + 0.2f); // (a double-click doesn't confirm)
                        yield return Shot("stuck-own-armed");
                        bool ownArmedOnly = _versusDeck == ownDeckBefore && profile.Dp == ownDpOffered && _scenes.ScoutingOpen;
                        if (ownQuote.Price > 0) Report("the first click only arms it (nothing spent yet)", ownArmedOnly);
                        if (ownArmedOnly) ownDeck();
                        yield return new WaitForSecondsRealtime(0.5f);
                        yield return Shot("stuck-own-bought");
                        var ownedName = _versusDeck ?? "";
                        var ownedBuilt = _library.Contains(ownedName) ? _library.Load(ownedName) : new Deck();
                        var ownLoanerList = _campaign.Loaner(m3Pick.Deck, cupM.After);
                        Report($"the second click buys it: {ownedName} is your deck, not a loaner, {ownDpOffered - profile.Dp:N0} DP spent",
                            ownedName != ownDeckBefore && profile.Deck == ownedName && !_campaign.IsLoanerDeck(ownedName) && ownDpOffered - profile.Dp == ownQuote.Price);
                        Report($"it's the loaner's list as measured ({ownedBuilt.Main.Count} + {ownedBuilt.Extra.Count}), and plays by {Playbooks.RecipeOf(ownedBuilt)}'s playbook",
                            ownQuote.StandIns > 0 || (ownedBuilt.Main.OrderBy(c => c).SequenceEqual(ownLoanerList.Main.OrderBy(c => c)) && ownedBuilt.Extra.OrderBy(c => c).SequenceEqual(ownLoanerList.Extra.OrderBy(c => c))));
                        var ownProblems = _campaign.Problems(ownedBuilt, cupM.After);
                        Report($"and it's ready to duel there ({(ownProblems.Count == 0 ? "no problems" : ownProblems[0].Message)})", ownProblems.Count == 0 && ownedBuilt.Side.Count == 0);
                        Report("the versus screen comes back with it picked and says what it did, with the advice closed",
                            _scenes.Buttons.ContainsKey("Duel!") && !_scenes.ScoutingOpen && Playbooks.RecipeOf(ownedBuilt) == ownQuote.Recipe);
                        var ownAgain = _campaign.QuoteOwn(m3Pick.Deck, cupM.After);
                        Report($"stuck there again, it offers the copy you own instead ({ownAgain?.Existing})", ownAgain?.Existing == ownedName && ownAgain.Price == 0);
                        _library.Delete(ownedName);
                        // A recipe whose title has a slash ("Fire King Snake-Eye / Azamina") is saved under a file-safe
                        // name; owning it must leave your deck pointing at the deck that was saved.
                        if (_campaign.QuoteOwn("loaner:fire-king-snake-eye", cupM.After) is { } slashQuote)
                        {
                            DebugAtLeastDp(profile, slashQuote.Price + 1000);
                            var slashName = _campaign.Own(slashQuote, cupM.After) ?? "";
                            Report($"owning a deck with a slash in its title leaves your deck one that exists ({slashName})",
                                slashName.Length > 0 && profile.Deck == slashName && _library.Contains(slashName));
                            if (slashName.Length > 0) _library.Delete(slashName);
                        }
                    }
                    profile.Deck = ownDeckBefore;
                    RestoreDp(profile, ownDpBefore);
                    _versusDeck = ownDeckBefore;
                    if (_scenes.Buttons.TryGetValue("Back", out var ownBack)) ownBack();
                    yield return new WaitForSecondsRealtime(0.3f);
                    _campaignScreen.Close();
                }
                else Report("the Master Tag Cup's match 3 advice has a loaner to own", false);
                profile.LossStreaks.Remove(m3Key);
            }
            profile.MatchWins.Clear();
            foreach (var (k, v) in winsBeforeMaster) profile.MatchWins[k] = v;
            profile.Wins.Clear();
            foreach (var (k, v) in beatenBeforeMaster) profile.Wins[k] = v;
            _campaign.Save();

            // Special guests: the lessons unlock Ines; a duel against her through the versus screen, conceded, records
            // a loss and shows the results, and Continue goes back to the guests.
            foreach (var l in ChainLessons.All) profile.Seen.Add(l.SeenKey);
            ShowGuests();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("guests");
            Report($"the guests screen offers a duel with Ines once the lessons are passed ({string.Join(", ", _scenes.Buttons.Keys)})",
                _scenes.Buttons.ContainsKey("Duel:ines") && _scenes.Buttons.ContainsKey("Tag:ines") && !_scenes.Buttons.ContainsKey("Duel:rhea"));
            // Scout before choosing to duel: a report on the guest's decks over the guests screen; Close keeps the screen.
            if (_scenes.Buttons.TryGetValue("Scout:ines", out var scoutInes))
            {
                scoutInes();
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("guest-scout");
                bool open = _scenes.ScoutingOpen;
                if (_scenes.Buttons.TryGetValue("Close scouting", out var closeGuestScout)) closeGuestScout();
                Report("Scout on the guests screen opens a report on their decks, and Close goes back to the guests", open && !_scenes.ScoutingOpen && _scenes.Buttons.ContainsKey("Duel:ines"));
            }
            else Report("the guests screen has a Scout button", false);
            if (_scenes.Buttons.TryGetValue("Duel:ines", out var fightInes))
            {
                fightInes();
                var versusUntil = Time.realtimeSinceStartup + 5;
                while (!_scenes.Buttons.ContainsKey("Duel!") && Time.realtimeSinceStartup < versusUntil) yield return null;
                yield return Shot("guest-versus");
                Report($"the guest versus screen has deck arrows and a Scout button ({string.Join(", ", _scenes.Buttons.Keys.Take(6))})",
                    _scenes.Buttons.Keys.Any(k => k.StartsWith(">@780")) && _scenes.Buttons.ContainsKey("Scout"));
                if (_scenes.Buttons.TryGetValue("Duel!", out var startGuest)) startGuest();
                yield return new WaitForSecondsRealtime(1.5f);
                int inesLosses = profile.Losses.TryGetValue("ines", out var il) ? il : 0;
                bool guestStarted = _guestDuel != null && _runner != null;
                ConcedeDuel();
                var guestShown = Time.realtimeSinceStartup + 8;
                while (!(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue")) && Time.realtimeSinceStartup < guestShown) yield return null;
                yield return Shot("guest-results");
                Report($"a guest duel starts, and conceding it records the loss and shows the results ({_scenes.LastResults?.Circuit})",
                    guestStarted && (profile.Losses.TryGetValue("ines", out var il2) ? il2 : 0) == inesLosses + 1 && (_scenes.LastResults?.Circuit.Contains("Special guest") ?? false));
                // The save's first loss: Gus's first-loss scene plays on the results' Continue (read it through), once ever.
                bool firstEver = inesLosses == 0 && profile.Losses.Values.Sum() == 1;
                if (_scenes.Buttons.TryGetValue("Continue", out var afterGuest)) afterGuest();
                var firstLossBy = Time.realtimeSinceStartup + 2;
                while (!_storyScreen.IsOpen && Time.realtimeSinceStartup < firstLossBy) yield return null;
                bool firstLossPlayed = _storyScreen.IsOpen;
                if (firstLossPlayed) { yield return new WaitForSecondsRealtime(2.6f); yield return Shot("first-loss"); }
                for (int k = 0; k < 40 && _storyScreen.IsOpen; k++) { _storyScreen.DebugAdvance(); yield return new WaitForSecondsRealtime(0.3f); }
                bool hasFirstLoss = _story.Scenes.Any(sc => sc.Id == "first-loss");
                Report($"the save's first loss ({firstEver}: {profile.Losses.Values.Sum()} recorded) plays first-loss on the results' Continue ({firstLossPlayed}, seen {profile.Seen.Contains("first-loss")}{(hasFirstLoss ? "" : "; no scene yet")})",
                    !hasFirstLoss || (firstEver ? firstLossPlayed && profile.Seen.Contains("first-loss") : !firstLossPlayed));
                yield return new WaitForSecondsRealtime(0.4f);
                Report("Continue goes back to the guests", _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel:ines"));
                // A second concede plays nothing.
                if (_scenes.Buttons.TryGetValue("Duel:ines", out var fightAgain))
                {
                    fightAgain();
                    var againBy = Time.realtimeSinceStartup + 5;
                    while (!_scenes.Buttons.ContainsKey("Duel!") && Time.realtimeSinceStartup < againBy) yield return null;
                    if (_scenes.Buttons.TryGetValue("Duel!", out var startAgain)) startAgain();
                    yield return new WaitForSecondsRealtime(1.5f);
                    bool againStarted = _guestDuel != null && _runner != null;
                    ConcedeDuel();
                    var againShown = Time.realtimeSinceStartup + 8;
                    while (!(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue")) && Time.realtimeSinceStartup < againShown) yield return null;
                    if (_scenes.Buttons.TryGetValue("Continue", out var afterAgain)) afterAgain();
                    var quietBy = Time.realtimeSinceStartup + 2;
                    while (!_storyScreen.IsOpen && Time.realtimeSinceStartup < quietBy) yield return null;
                    bool secondPlayed = _storyScreen.IsOpen;
                    for (int k = 0; k < 40 && _storyScreen.IsOpen; k++) { _storyScreen.DebugAdvance(); yield return new WaitForSecondsRealtime(0.3f); }
                    yield return new WaitForSecondsRealtime(0.4f);
                    Report($"a second concede to Ines plays no scene ({againStarted}, {secondPlayed}) and goes back to the guests",
                        againStarted && !secondPlayed && _scenes.IsOpen && _scenes.Buttons.ContainsKey("Duel:ines"));
                }
                if (_scenes.Buttons.TryGetValue("Back", out var guestsBack)) guestsBack();
            }
            _campaignScreen.Close();

            // Structure decks: on sale for the shop's era; buying one adds its cards and saves it as a campaign deck.
            DebugAddDp(profile, 5000);
            int onSale = _structures.DebugOnSale(), before2 = profile.Collection.Total;
            _structures.Open();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("structure-decks");
            _structures.DebugBuyFirst();
            _library.Refresh();
            Report($"{onSale} structure decks on sale; bought {_structures.LastBought} (+{profile.Collection.Total - before2} cards, saved as a campaign deck)",
                onSale > 0 && profile.Collection.Total > before2 && _library.IsCampaign(_structures.LastBought));
            _structures.Close();
            _shop.Close();
            _campaignScreen.Close();

            // New Game+: circuits reset, everything earned stays.
            int owned = profile.Collection.Total, dpNow = profile.Dp, levelNow = profile.Level;
            _campaignScreen.DebugNewGamePlus();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("new-game-plus");
            Report($"New Game+ {profile.Cycle}: circuits reset; {owned} cards, {dpNow} DP and level {levelNow} kept",
                profile.Cycle == 1 && profile.MatchWins.Count == 0 && profile.Collection.Total == owned && profile.Dp == dpNow
                && profile.Level == levelNow && _campaign.Circuits.Current(profile) == 0);
            // The coin toss (checks skip it otherwise): Duel! tosses a coin and the winner chooses who starts. Won: Go
            // second, and the opponent takes turn 1. Lost: the opponent's choice (their deck goes first) starts the duel.
            _autoToss = false;
            for (int line = 0; line < 40 && _storyScreen.IsOpen; line++) { _storyScreen.DebugAdvance(); yield return new WaitForSecondsRealtime(0.2f); }
            foreach (bool youWin in new[] { true, false })
            {
                _campaignScreen.Close(); // the campaign screen holds a duel (a real Duel! comes from the versus screen alone)
                seed = Enumerable.Range(seed + 1, 50).First(sd => DebugLeftWinsToss(sd + 1) == youWin); // Duel! adds one
                ShowVersus(0, 0);
                yield return new WaitForSecondsRealtime(0.4f);
                if (_scenes.Buttons.TryGetValue("Duel!", out var tossDuel)) tossDuel();
                if (youWin) { yield return new WaitForSecondsRealtime(0.45f); yield return Shot("coin-toss-flip"); }
                var tossShown = Time.realtimeSinceStartup + 4;
                while (Time.realtimeSinceStartup < tossShown && !(_scenes.Buttons.ContainsKey("Go second") || _scenes.Buttons.ContainsKey("Continue"))) yield return null;
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot(youWin ? "coin-toss-won" : "coin-toss-lost");
                int wantFirst;
                if (youWin)
                {
                    Report("winning the toss offers Go first and Go second", _scenes.Buttons.ContainsKey("Go first") && _scenes.Buttons.ContainsKey("Go second"));
                    wantFirst = 1;
                    if (_scenes.Buttons.TryGetValue("Go second", out var second)) second();
                }
                else
                {
                    var theirDeck = _campaign.Circuits.All[0].Matches[0].Deck;
                    wantFirst = Ygo.Engine.AI.Playbooks.Playbooks.GoesFirst(theirDeck) ? 1 : 0;
                    Report("losing the toss shows their choice with Continue", _scenes.Buttons.ContainsKey("Continue") && !_scenes.Buttons.ContainsKey("Go first"));
                    if (_scenes.Buttons.TryGetValue("Continue", out var cont)) cont();
                }
                // Who the engine gave turn 1 (recorded as the duel starts: without animations a whole turn can pass in a
                // frame, or even before the first one).
                var startedBy = Time.realtimeSinceStartup + 15;
                while (Time.realtimeSinceStartup < startedBy && FirstTurnPlayer < 0) yield return null;
                int turnOne = FirstTurnPlayer;
                Report($"{(youWin ? "won, chose to go second" : "lost the toss")}: turn 1 is {(turnOne == 0 ? "yours" : turnOne == 1 ? "theirs" : "not started")}",
                    turnOne == wantFirst);
                ConcedeDuel();
                var tossResults = Time.realtimeSinceStartup + 8;
                while (Time.realtimeSinceStartup < tossResults && !(_scenes.IsOpen && _scenes.Buttons.ContainsKey("Continue"))) yield return null;
                if (_scenes.Buttons.TryGetValue("Continue", out var tossBack)) tossBack();
                yield return new WaitForSecondsRealtime(0.4f);
                for (int line = 0; line < 40 && _storyScreen.IsOpen; line++) { _storyScreen.DebugAdvance(); yield return new WaitForSecondsRealtime(0.2f); }
            }
            _autoToss = true;
            _campaignScreen.Close();

            // The app closed mid-duel: a circuit match and a Locals game load as losses (DuelController.LiveDuel).
            yield return LiveDuelCampaignCheck(Report, Shot);
            // Original texts at the table: the editor's note, a modern circuit's versus and duel, the Goat Cup (DuelController.TextsCheck).
            yield return TextsCampaignCheck(Report, Shot);

            // Start over: the save is backed up and the starter screen comes back.
            var saveDir = Path.GetDirectoryName(_campaign.SavePath)!;
            int backups = Directory.GetFiles(saveDir, "profile-*.txt").Length;
            // The old run's story and notice never carry into the next game: queued here, gone after Start over.
            _campaign.QueueAlleyStory(new[] { "check:startover" }, new Dictionary<string, string>());
            _campaign.AlleyNotice = "A notice from the old run.";
            bool queuedBefore = _campaign.AlleyNoticeEvents.Contains("check:startover");
            _campaign.StartOver();
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.4f);
            Report("Start over backs up the save and asks for a starter deck",
                _campaign.Profile == null && Directory.GetFiles(saveDir, "profile-*.txt").Length == backups + 1 && !File.Exists(_campaign.SavePath));
            Report($"Start over drops the old run's queued story and notice (queued before: {queuedBefore})",
                queuedBefore && _campaign.AlleyNoticeEvents.Count == 0 && _campaign.AlleyNotice.Length == 0);
            _campaignScreen.Close();
            Application.Quit();
        }

        private System.Collections.IEnumerator MatCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            void Report(string what, bool ok) => Debug.Log($"mat check: {(ok ? "ok  " : "FAIL")} {what}");
            yield return new WaitForSecondsRealtime(1f);
            _title.Close();
            _menu.Close();
            int Find(string id) => Math.Max(0, _roster.FindIndex(p => p.Id == id));
            _pickCharacter[0] = Find("nyx"); _pickDeck[0] = "zombies";
            _pickCharacter[1] = Find("old-gus"); _pickDeck[1] = "fury-from-the-deep";
            seed = 11;
            NewDuel();
            _paused = false;
            speedIndex = 5;
            UpdateControls();
            yield return new WaitForSecondsRealtime(2f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "mat-start.png"));
            bool field = false;
            var deadline = Time.realtimeSinceStartup + 200;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (_board.Players.Any(p => p.SpellTraps.Count > 5 && p.SpellTraps[5] is FieldCard f && f.IsFaceUp)) { field = true; break; }
                if (_announcedResult) { yield return new WaitForSecondsRealtime(0.5f); seed++; NewDuel(); _paused = false; UpdateControls(); }
            }
            yield return new WaitForSecondsRealtime(2.5f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "mat-field.png"));
            Report("a Field Spell was played and its side of the mat changed", field);

            // Every signature mat on one sheet (two per row, in mats.txt order), saved as a picture.
            var ids = Playmat.Signatures.Keys.ToList();
            const int w = 512, h = 192, gap = 8;
            int rows = (ids.Count + 1) / 2;
            var sheet = new Texture2D(w * 2 + gap, rows * (h + gap), TextureFormat.RGBA32, false);
            sheet.SetPixels(Enumerable.Repeat(Color.black, sheet.width * sheet.height).ToArray());
            for (int i = 0; i < ids.Count; i++)
            {
                var sig = Playmat.Signatures[ids[i]];
                var tex = Playmat.Painted(sig.Theme, 1, sig.Motif);
                sheet.SetPixels((i % 2) * (w + gap), (rows - 1 - i / 2) * (h + gap), w, h, tex.GetPixels());
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(folder, "mats-sheet.png"), sheet.EncodeToPNG());
            File.WriteAllLines(Path.Combine(folder, "mats-sheet.txt"), ids);
            Report($"{ids.Count} signature mats painted", ids.Count >= 20);

            // Bosses face off on their mats (pictures from art/mats where there are some).
            var pairs = new[] { ("vera", "warriors", "burning-abyss"), ("gouki", "gouki", "maliss"), ("true-draco", "true-draco", "unchained"),
                ("dragon-ruler", "dragon-ruler", "altergeist"), ("infernity", "infernity", "branded-despia"), ("burning-abyss", "burning-abyss", "ishizu-tearlaments") };
            foreach (var (bottom, bottomDeck, top) in pairs)
            {
                _pickCharacter[0] = Find(bottom); _pickDeck[0] = bottomDeck;
                _pickCharacter[1] = Find(top); _pickDeck[1] = top;
                seed = 3;
                NewDuel();
                _paused = false;
                UpdateControls();
                yield return new WaitForSecondsRealtime(2.5f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"mat-{bottom}-vs-{top}.png"));
                Report($"{bottom} vs {top}: pictures {(Playmat.LoadCustom(bottom) != null ? "yes" : "no")}/{(Playmat.LoadCustom(top) != null ? "yes" : "no")}", true);
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private System.Collections.IEnumerator BoardCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            void Report(string what, bool ok) => Debug.Log($"board check: {(ok ? "ok  " : "FAIL")} {what}");
            System.Collections.IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"board-{name}.png"));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            _title.Close();
            _menu.Close();
            Report($"screen {Screen.width}x{Screen.height}", true);
            _pickCharacter[0] = -1;
            _pickDeck[0] = "classic";
            var cliArgs = Environment.GetCommandLineArgs();
            int deckArg = Array.IndexOf(cliArgs, "-deck");
            if (deckArg >= 0 && deckArg + 1 < cliArgs.Length) _pickDeck[0] = cliArgs[deckArg + 1]; // `-deck <name>`: play the check with another deck
            seed = 7;
            int openingBefore = _view.Fx.OpeningShuffles;
            NewDuel();
            _paused = false;
            speedIndex = 5; // 16 events/s: the opponent's turns fly by; your decisions wait for the check anyway
            UpdateControls();
            // Chain prompts: the pill cycles Auto -> On -> Off -> Auto and the human seat follows it.
            var startMode = ResponseSetting;
            var seen = new List<string>();
            for (int i = 0; i < 3; i++) { seen.Add($"{_human?.Presenter.Mode}: {System.Text.RegularExpressions.Regex.Replace(_view.ResponsesText, "<[^>]+>", "")}"); CycleResponses(); }
            Report($"the chain prompt pill cycles ({string.Join(" | ", seen)})", ResponseSetting == startMode && seen.Distinct().Count() == 3);
            if (_human != null) _human.Presenter.Mode = ResponseMode.On; // this duel only: the check wants every chance to respond
            var rng = new System.Random(3);
            bool sawZone = false, sawMenu = false, sawChain = false;
            var sawDialogs = new HashSet<string>();
            var waitingSeen = "";
            bool sawDrop = false;
            var deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline && !_announcedResult)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                var set = _view.Prompt.Current;
                if (set == null && waitingSeen.Length == 0 && _view.WaitingText.Length > 0) waitingSeen = _view.WaitingText;
                if (set == null || _runner == null || !_runner.IsWaiting) continue;
                // Card-first: on your turn, clicking a hand card opens its menu at the card.
                if (!sawMenu && set.Prompt is Ygo.Engine.Protocol.IdlePrompt && set.Choices.FirstOrDefault(c => c.Card is { } r && r.Location == Location.Hand) is Choice handChoice)
                {
                    sawMenu = true;
                    var r = handChoice.Card!.Value;
                    var labels = _view.DebugClickCard(r.Controller, r.Location, (int)r.Sequence);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("card-menu");
                    Report($"clicking {_cardPack!.Get(r.Code)?.Name} in hand offers: {string.Join(", ", labels)}", labels.Count > 0);
                    // A remapped controller (Back on Y, in memory only): pressing it closes the menu and the cursor
                    // goes back to the card.
                    ControllerBindings.Persist = false;
                    ControllerBindings.Reload();
                    ControllerBindings.Bind(PadAction.Back, "buttonNorth");
                    Report($"binding Back to Y swaps it with the extra action (now {ControllerBindings.Label(PadAction.Extra)}) and the legend follows",
                        ControllerBindings.Get(PadAction.Extra) == "buttonEast" && ControllerBindings.Legend().Contains("Y  back"));
                    var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("CheckPad");
                    pad.MakeCurrent();
                    _controller.DebugMoveTo(new Vector2(40, 40));
                    yield return null;
                    // Queued, so the press lands in the next frame's input update like a real one.
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.North));
                    yield return null;
                    yield return null;
                    var focus = _view.FocusPoint;
                    Report($"pressing the new Back button closes the card's menu and returns the cursor to it ({_controller.CursorPosition} vs {focus})",
                        !_view.ActionMenuOpen && focus is Vector2 fp && Vector2.Distance(fp, _controller.CursorPosition) < 2f);
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                    yield return null;
                    UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);
                    ControllerBindings.Reset();
                    ControllerBindings.Persist = true;
                    ControllerBindings.Reload();
                    _view.CloseActionMenu();
                }
                // Drag to play: drop a monster from your hand on your second Main Monster Zone; it should be Summoned there.
                if (sawMenu && !sawDrop && set.Prompt is Ygo.Engine.Protocol.IdlePrompt
                    && set.Choices.FirstOrDefault(c => c.Verb == "Summon" && c.Card is { } hc && hc.Location == Location.Hand) is Choice summon
                    && (_board.Players[0].Monsters.Count < 2 || _board.Players[0].Monsters[1] == null))
                {
                    sawDrop = true;
                    var dropped = summon.Card!.Value;
                    _view.DebugDropHandCard((int)dropped.Sequence, Location.MZone, 1);
                    var until = Time.realtimeSinceStartup + 8;
                    while (Time.realtimeSinceStartup < until && !(_board.Players[0].Monsters.Count > 1 && _board.Players[0].Monsters[1]?.Code == dropped.Code)) yield return null;
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("drag-drop");
                    Report($"dropping {_cardPack!.Get(dropped.Code)?.Name} on a Monster Zone summons it there",
                        _board.Players[0].Monsters.Count > 1 && _board.Players[0].Monsters[1]?.Code == dropped.Code);
                    continue;
                }
                if (!sawChain && set.IsChain && set.CanCancel)
                {
                    sawChain = true;
                    if (_narrator?.Chain.Links.Count > 0)
                        Report($"the chain panel lists the chain ({string.Join(" / ", _view.ChainRows)})", _view.ChainRows.Count >= 2);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("chain");
                    Report($"a chance to respond opens the chain dialog ({set.Title})", _view.Dialogs.Kind == "chain");
                }
                // The other on-board dialogs, once each: card picker (with a pick shown), yes/no, options.
                var kind = _view.Dialogs.Kind;
                if (kind.Length > 0 && kind != "chain" && sawDialogs.Add(kind))
                {
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot($"dialog-{kind}");
                    if (kind == "picker" && set.Mode == ChoiceMode.Multi)
                    {
                        _view.Prompt.ClickChoice(0);
                        yield return new WaitForSecondsRealtime(0.3f);
                        yield return Shot("dialog-picker-picked");
                        Report($"picking a card in the picker marks it ({set.Title})", _view.Prompt.IsSelected(0));
                        _view.Prompt.ClickChoice(0);
                    }
                    _view.Dialogs.SetHidden(true);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Shot($"dialog-{kind}-hidden");
                    Report($"the {kind} dialog opens on the board and hides to show the board ({set.Title})", _view.Dialogs.Hidden);
                    _view.Dialogs.SetHidden(false);
                }
                if (!sawZone && set.Title.StartsWith("Choose a zone"))
                {
                    sawZone = true;
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("zones");
                    Report($"a zone choice lights up {set.Choices.Count} zones on the board", set.Choices.All(c => c.Card != null));
                }
                if (set.CanCancel && rng.NextDouble() < 0.3) { _view.Prompt.CancelSelection(); continue; }
                if (set.Mode == ChoiceMode.Multi)
                {
                    foreach (var i in Enumerable.Range(0, set.Choices.Count).Take(Math.Max(set.Min, 1))) _view.Prompt.ClickChoice(i);
                    _view.Prompt.Confirm();
                }
                else if (set.Mode == ChoiceMode.Incremental && set.CanFinish && rng.NextDouble() < 0.5) _view.Prompt.FinishSelection();
                else
                {
                    var open = Enumerable.Range(0, set.Choices.Count).Where(i => !set.Choices[i].AlreadySelected).ToList();
                    _view.Prompt.ClickChoice(open.Count > 0 ? open[rng.Next(open.Count)] : 0);
                }
                if (sawZone && sawMenu && ((sawChain && sawDialogs.Contains("picker")) || _board.Turn > 12) && _board.Players[0].Graveyard.Count + _board.Players[1].Graveyard.Count >= 3) break;
            }
            Report("you were asked where to place a card", sawZone);
            Report($"the duel-start riffle played once per Deck ({_view.Fx.OpeningShuffles - openingBefore})", _view.Fx.OpeningShuffles - openingBefore == 2);
            Report($"while waiting the board says so (\"{waitingSeen}\")", waitingSeen.Length > 0);
            _paused = true;
            UpdateControls();
            int owner = _board.Players[1].Graveyard.Count >= _board.Players[0].Graveyard.Count ? 1 : 0;
            int inGraveyard = _board.Players[owner].Graveyard.Count; // counted when the viewer opens (an effect may move one later)
            int shown = _view.DebugBrowse(owner, Location.Grave);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("graveyard");
            Report($"the Graveyard viewer shows {shown} of {inGraveyard} cards", shown == inGraveyard);
            _view.ClosePileViewer();

            // Deck shuffles: each Deck riffles once (two of the same player's in one batch merge) and leaves nothing behind;
            // at Tempo 0 (fast-forward, skip) nothing plays.
            int riffles = 0;
            void OnRiffle(string fx) { if (fx == "shuffle") riffles++; }
            _view.Fx.Started += OnRiffle;
            var shuffles = new[]
            {
                new DuelEvent { Kind = Ygo.Engine.Presentation.DuelEventKind.ShuffleDeck, Player = 0 }, new DuelEvent { Kind = Ygo.Engine.Presentation.DuelEventKind.ShuffleDeck, Player = 0 },
                new DuelEvent { Kind = Ygo.Engine.Presentation.DuelEventKind.ShuffleDeck, Player = 1 },
            };
            var tempoBefore = _view.Fx.Tempo;
            _view.Fx.Tempo = 1;
            _view.Fx.Play(shuffles);
            yield return new WaitForSecondsRealtime(0.09f); // the halves apart (a repeat: 0.28 s in all)
            yield return Shot("shuffle");
            int midRiffle = _view.Fx.ShuffleGhosts;
            Debug.Log($"riffle cards mid-riffle: {_view.Fx.ShuffleSpots}");
            yield return new WaitForSecondsRealtime(0.17f); // the opponent's riffle follows yours (60% of it in)
            yield return Shot("shuffle-top");
            Debug.Log($"riffle cards mid-riffle (top): {_view.Fx.ShuffleSpots}");
            yield return new WaitForSecondsRealtime(0.8f);
            yield return null;
            Report($"a Deck shuffle riffles each Deck once ({riffles} riffles, {midRiffle} cards mid-riffle) and leaves no cards behind ({_view.Fx.ShuffleGhosts} left)",
                riffles == 2 && midRiffle == 8 && _view.Fx.ShuffleGhosts == 0);
            riffles = 0;
            _view.Fx.Tempo = 0;
            _view.Fx.Play(shuffles);
            yield return null;
            Report($"nothing riffles at Tempo 0 ({riffles} riffles, {_view.Fx.ShuffleGhosts} cards)", riffles == 0 && _view.Fx.ShuffleGhosts == 0);
            _view.Fx.Tempo = tempoBefore;
            _view.Fx.Started -= OnRiffle;

            // Previews of decisions that rarely come up by chance (shown, never answered), then the real one again.
            var pending = _view.Prompt.Current;
            var options = new ChoiceSet { Title = "Choose an effect", Prompt = new Ygo.Engine.Protocol.OptionPrompt() };
            options.Choices.Add(new Choice { Label = "Increase the Level of the monster by 1." });
            options.Choices.Add(new Choice { Label = "Special Summon 1 \"Resonator\" monster from your hand." });
            _view.ShowChoices(options, false);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("preview-options");
            _view.Dialogs.SelectTab(1);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("preview-options-tab2");
            Report("\"Use which effect?\" shows numbered tabs", _view.Dialogs.Kind == "options");

            var deckCards = _library.Load(_pickDeck[0]).Main.Distinct().Take(7).ToList();
            var pick = new ChoiceSet { Title = "Select the card(s) to add from your Deck to your hand", Mode = ChoiceMode.Multi, Min = 2, Max = 2,
                Prompt = new Ygo.Engine.Protocol.SelectCardPrompt { Min = 2, Max = 2 } };
            for (int i = 0; i < deckCards.Count; i++)
                pick.Choices.Add(new Choice { Label = _cardPack!.Get(deckCards[i])?.Name ?? "", Card = new Ygo.Engine.Protocol.CardRef(deckCards[i], 0, Location.Deck, (uint)i) });
            _view.ShowChoices(pick, false);
            _view.Prompt.ClickChoice(0);
            _view.Prompt.ClickChoice(2);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("preview-picker-multi");
            Report("a 2-card pick from the Deck marks both picks", _view.Prompt.IsSelected(0) && _view.Prompt.IsSelected(2) && _view.Dialogs.Kind == "picker");

            var onField = new List<Ygo.Engine.Protocol.CardRef>();
            for (byte p = 0; p < 2; p++)
                for (int z = 0; z < _board.Players[p].Monsters.Count; z++)
                    if (_board.Players[p].Monsters[z] is FieldCard fc) onField.Add(new Ygo.Engine.Protocol.CardRef(fc.Code, p, Location.MZone, (uint)z));
            if (onField.Count >= 2)
            {
                var fieldPick = new ChoiceSet { Title = "Select the monsters to use as material", Mode = ChoiceMode.Multi, Min = 2, Max = 2,
                    Prompt = new Ygo.Engine.Protocol.SelectCardPrompt { Min = 2, Max = 2 } };
                foreach (var c in onField) fieldPick.Choices.Add(new Choice { Label = _cardPack!.Get(c.Code)?.Name ?? "", Card = c });
                _view.ShowChoices(fieldPick, false);
                _view.Prompt.ClickChoice(0);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("preview-field-multi");
                Report("a 2-card field pick glows and marks the first pick", _view.Prompt.IsSelected(0) && _view.Dialogs.Kind == "field");
            }

            // The hand fan with a card lifted under the pointer, and life points in danger.
            _view.ShowChoices(pending, false);
            if (_board.Players[0].Hand.Count > 0)
            {
                _view.DebugHoverHandCard(_board.Players[0].Hand.Count / 2);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("hand-hover");
            }
            int lpBefore = _board.Players[0].LifePoints;
            _view.DebugLifePoints(0, 800);
            yield return new WaitForSecondsRealtime(0.25f);
            yield return Shot("lp-hit");
            yield return new WaitForSecondsRealtime(1.6f);
            yield return Shot("lp-danger");
            Report("life points can drop into the danger zone", true);
            _view.DebugLifePoints(0, lpBefore);

            // The opponent reacts: each moment once, with their own line (and expression art when there is some).
            var rival = _roster.FirstOrDefault(r => r.Id == _view.PortraitId(1));
            if (rival != null)
                foreach (var kind in new[] { "summon", "hit", "danger" })
                {
                    _view.React(1, kind, Reactions.Line(rival, kind, new System.Random(1)));
                    yield return new WaitForSecondsRealtime(0.45f);
                    yield return Shot($"react-{kind}");
                    Report($"{rival.Name} reacts to \"{kind}\": {_view.LastReaction}", _view.LastReaction.StartsWith($"1:{kind}:"));
                    yield return new WaitForSecondsRealtime(2.9f);
                }

            // Pin: the detail keeps its card while another one shows in the second panel.
            var deckInfos = _library.Load(_pickDeck[0]).Main.Select(c => _cardPack!.Get(c)).Where(i => i != null).Distinct().Take(2).ToList();
            if (deckInfos.Count == 2)
            {
                _view.ShowInfo(deckInfos[0]!);
                _view.TogglePin();
                _view.ShowInfo(deckInfos[1]!);
                var (pinnedName, hoveredName) = _view.PinState;
                Report($"pinning keeps {pinnedName} while {hoveredName} shows beside it", pinnedName == deckInfos[0]!.Name && hoveredName == deckInfos[1]!.Name);
                // The duel keeps playing meanwhile; whatever it shows goes to the second panel, never over the pin.
                yield return new WaitForSecondsRealtime(0.4f);
                _view.ShowInfo(deckInfos[1]!);
                yield return Shot("pinned");
                Report("the pinned card stays while the duel plays on", _view.PinState.pinned == deckInfos[0]!.Name);
                _view.TogglePin();
            }

            // The card detail panel with a monster from the deck.
            var monster = _library.Load(_pickDeck[0]).Main.Select(c => _cardPack!.Get(c)).FirstOrDefault(i => i != null && (i.Type & CardType.Monster) != 0);
            _view.ShowChoices(pending, false);
            if (monster != null) _view.ShowInfo(monster);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("detail");
            Report($"the card detail shows {monster?.Name}", monster != null && _view.Detail.Text.Length > 0);
            Application.Quit();
        }

        private System.Collections.IEnumerator TagCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            void Report(string what, bool ok) => Debug.Log($"tag check: {(ok ? "ok  " : "FAIL")} {what}");
            System.Collections.IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"tag-{name}.png"));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
            _title.Close();
            _menu.Close();
            _tagSetup.Open();
            var lineup = _tagSetup.DebugLineup;
            // Four characters (no human seat), so the duel plays itself.
            lineup.Characters[0] = Enumerable.Range(0, _roster.Count).First(i => IsFreeCharacter(i) && Array.IndexOf(lineup.Characters, i) < 0);
            lineup.Decks[0] = DeckChoices(_roster[lineup.Characters[0]])[0];
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("setup");
            Report($"the setup offers four seats: {string.Join(", ", lineup.Characters.Select(c => _roster[c].Name))}",
                _tagSetup.IsOpen && lineup.Characters.Distinct().Count() == 4);

            int swaps = 0;
            _tagSetup.DebugStart();
            _runner!.MessageReceived += m => { if (m[0] == Msg.TagSwap) swaps++; };
            speedIndex = Mathf.Min(Speeds.Length - 1, 3);
            UpdateControls();
            var deadline = Time.realtimeSinceStartup + 60;
            while (swaps < 3 && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("duel");
            // The lineup is random: a duel can be over before two swaps (a first-turn kill), and that's fine too.
            Report($"the tag duel is under way ({swaps} swaps, turn {_board.Turn}{(_runner?.IsFinished == true ? ", already over" : "")})",
                (swaps >= 2 || _runner?.IsFinished == true) && _tag != null);

            speedIndex = Speeds.Length - 1;
            UpdateControls();
            deadline = Time.realtimeSinceStartup + 240;
            while (!_announcedResult && Time.realtimeSinceStartup < deadline) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("result");
            Report($"the tag duel finished (winner team {_board.Winner}, turn {_board.Turn})", _announcedResult);
            var recent = _replays.All().Where(e => e.Recent).OrderByDescending(e => e.Replay.Date).FirstOrDefault();
            Report($"its replay is saved as a tag duel: {recent?.Replay.Title}", recent != null && recent.Replay.IsTag);
            Application.Quit();
        }

        private System.Collections.IEnumerator MenuCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"menu-{name}.png"));
                yield return null;
                yield return null;
            }
            void Report(string what, bool ok) => Debug.Log($"menu check: {(ok ? "ok  " : "FAIL")} {what}");
            yield return new WaitForSecondsRealtime(1.5f);
            Report("the game opens on the title screen", _title.IsOpen);
            yield return Shot("title");
            _title.DebugAttract();
            yield return new WaitForSecondsRealtime(5f);
            Report("attract mode plays a duel behind the title", !_paused && _ticks > 0);
            yield return Shot("attract");
            _title.DebugPress();
            yield return new WaitForSecondsRealtime(0.8f);
            Report("a button press opens the menu (and pauses the duel)", _menu.IsOpen && !_title.IsOpen && _paused);
            yield return Shot("main");
            yield return new WaitForSecondsRealtime(1.2f);
            var sound = Sounds.DebugState();
            Report($"sounds: {sound.clips} clips built; music \"{sound.music}\" {(sound.playing ? "playing" : "silent")} on the menu; "
                   + $"{(sound.listening ? "an audio listener is active" : "NO audio listener (nothing would be heard)")}",
                sound.clips == Ygo.Engine.Audio.Sfx.Effects.Count + Ygo.Engine.Audio.Sfx.Music.Count + ShowSounds.Count && sound.music == "menu" && sound.playing && sound.listening);
            // What actually reaches the speakers: the listener's output level while music and an effect play.
            Sounds.Play("summon_big");
            float loudest = 0;
            var output = new float[1024];
            for (int f = 0; f < 30; f++)
            {
                AudioListener.GetOutputData(output, 0);
                loudest = Mathf.Max(loudest, output.Max(x => Mathf.Abs(x)));
                yield return null;
            }
            Report($"sound reaches the output (peak level {loudest:F2})", loudest > 0.01f);
            _menu.Close();
            _settings.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("settings");
            _settings.DebugScroll(0.68f); // the Duel Presentation rows
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("settings-presentation");
            // The controller rows, one waiting for a button press (in memory only).
            ControllerBindings.Persist = false;
            ControllerBindings.BeginCapture(PadAction.Back, () => { });
            _settings.Open();
            _settings.DebugScroll(0f);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("settings-controller");
            ControllerBindings.EndCapture(null);
            ControllerBindings.Persist = true;
            _settings.Close();
            var free = FreeCharacters().Select(i => _roster[i].Name).ToList();
            Report($"free play offers {free.Count} of {_roster.Count} duelists: {string.Join(", ", free)}", free.Count < _roster.Count && free.Count >= _roster.Count / 2);
            Application.Quit();
        }

        /// <summary>Checks: log decisions that waited on screen (see the Answered handler).</summary>
        private static bool PromptTiming;
        private float _promptShownAt;
        private string _promptShownTitle = "";

        private System.Collections.IEnumerator ControlsCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"controls-{name}.png"));
                yield return null;
                yield return null;
            }
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"controls check: {(ok ? "ok  " : "FAIL")} {what}"); }
            // Answers any decision but a Main or Battle Phase one simply (no chains, "No", the first pick).
            void AnswerOthers()
            {
                var set = _view.Prompt.Current;
                if (set == null || set.Prompt is Ygo.Engine.Protocol.IdlePrompt || set.Prompt is Ygo.Engine.Protocol.BattlePrompt) return;
                if (set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                else if (set.Prompt is Ygo.Engine.Protocol.EffectYesNoPrompt || set.Prompt is Ygo.Engine.Protocol.YesNoPrompt)
                    _view.Prompt.ClickChoice(Math.Max(0, set.Choices.FindIndex(c => c.Label == "No")));
                else if (set.Mode == ChoiceMode.Single) _view.Prompt.ClickChoice(0);
                else { for (int i = 0; i < Math.Max(1, set.Min); i++) _view.Prompt.ClickChoice(i); _view.Prompt.Confirm(); }
            }
            // Waits for your Main Phase (1 or 2, as asked), answering anything else.
            System.Collections.IEnumerator WaitForMain(ushort phase, float seconds)
            {
                var until = Time.realtimeSinceStartup + seconds;
                while (Time.realtimeSinceStartup < until)
                {
                    var set = _view.Prompt.Current;
                    if (set?.Prompt is Ygo.Engine.Protocol.IdlePrompt && _board.Phase == phase && _board.TurnPlayer == 0) yield break;
                    if (set != null && !(set.Prompt is Ygo.Engine.Protocol.IdlePrompt) && !(set.Prompt is Ygo.Engine.Protocol.BattlePrompt)) AnswerOthers();
                    else if (set?.Prompt is Ygo.Engine.Protocol.BattlePrompt && _board.TurnPlayer == 0 && phase == Phase.Main2) _view.GoToPhase("main2");
                    yield return null;
                }
            }
            yield return new WaitForSecondsRealtime(0.5f);
            const uint blueEyes = 89631139, luster = 11091375, kuriboh = 40640057, prison = 70342110;
            _checkSetup = new List<PlacedCard>
            {
                // Set for the opponent: the first attack is cut short (the attacker banished), and its arrow must go too.
                new PlacedCard(1, prison, Location.SZone, 2, Ygo.Engine.Position.FaceDownAttack | Ygo.Engine.Position.FaceDownDefense),
                new PlacedCard(0, blueEyes, Location.MZone, 1, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(0, blueEyes, Location.MZone, 3, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(1, luster, Location.MZone, 2, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(1, kuriboh, Location.MZone, 1, Ygo.Engine.Position.FaceUpDefense),
            };
            _pickCharacter[0] = -1;
            _pickDeck[0] = "classic";
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == "rookie-ryo"));
            _pickDeck[1] = "classic";
            seed = 21;
            _paused = false;
            _nextFirstPlayer = 0; // a board set for your turn 1 (even with -gosecond)
            NewDuel();
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            yield return WaitForMain(Phase.Main1, 30);

            // Turn 1: no Battle Phase yet; the bar offers Main 2? no (it's through the Battle Phase), End yes.
            _view.OpenPhasePicker();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("picker-turn1");
            var lit = _view.DebugPhasePicker();
            Report($"turn 1, the phase bar lights only what you can do ({string.Join(", ", lit)})", lit.SequenceEqual(new[] { "End" }));
            var pinInfo = _cardPack!.Get(blueEyes)!;
            // The card detail's tags: the Spell Speed, then what the card does.
            foreach (var (name, want) in new[] { ("Mystical Space Typhoon", "Spell Speed 2,Destroy S/T"), ("Solemn Judgment", "Spell Speed 3,Negate"),
                         ("Mekk-Knight Purple Nightfall", "Spell Speed 2,Search"), ("Blue-Eyes White Dragon", "") })
            {
                var card = _cardPack!.All.FirstOrDefault(c => c.Name == name);
                if (card == null) { Report($"the card {name}", false); continue; }
                _view.ShowInfo(card);
                yield return null;
                var tags = _view.DetailTags;
                var wanted = want.Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (name == "Mekk-Knight Purple Nightfall") yield return Shot("tags");
                Report($"{name}'s tags: {(tags.Count == 0 ? "none" : string.Join(", ", tags))}", wanted.Length == 0 ? tags.Count == 0 : wanted.All(tags.Contains) && tags[0] == wanted[0]);
            }
            // Column guides: reading a Mekk-Knight shows the columns and their counts. Here: your Blue-Eyes in zones 1 and 3,
            // their Luster Dragon and Dimensional Prison facing your zone 2, their Kuriboh facing your zone 3. The middle
            // column has 2 cards and your zone there is free (summon here); the 4th has 2 but your Blue-Eyes is in it.
            _view.ClosePhasePicker();
            if (_cardPack.All.FirstOrDefault(c => c.Name == "Mekk-Knight Purple Nightfall") is { } nightfall)
            {
                _view.ShowInfo(nightfall);
                yield return null;
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("columns");
                var guide = _view.ColumnGuide;
                Report($"reading Purple Nightfall shows the columns: {string.Join(" ", guide.counts)} cards, gold {string.Join(" ", guide.ready.Select(r => r ? "Y" : "-"))}",
                    guide.card == nightfall.Name && guide.counts.SequenceEqual(new[] { 0, 1, 2, 2, 0 }) && guide.ready.SequenceEqual(new[] { false, false, true, false, false }));
                // In your Main Phase the gold column says you can summon there.
                Report($"in your Main Phase the gold column says \"summon here\" ({string.Join(" | ", _view.ColumnLabels)})",
                    _view.ColumnLabels.Count == 5 && _view.ColumnLabels[2].Contains("summon here") && _view.ColumnLabels.Count(c => c.Contains("summon here")) == 1);
                // The controller's Card terms button steps through the explained words of the card being read.
                _view.ShowInfo(nightfall);
                yield return null;
                yield return new WaitForSecondsRealtime(0.2f);
                var first = _view.NextTerm();
                var second = _view.NextTerm();
                yield return Shot("card-terms");
                Report($"the controller's Card terms ({ControllerBindings.Label(PadAction.Terms)}) explains the card's terms in turn ({first}, then {second})",
                    first != null && second != null && first != second && TermTooltip.Shown == second);
                _view.ShowInfo(pinInfo);
                yield return null;
                Report("and reading a card that isn't about columns hides them", _view.ColumnGuide.card == null);
            }
            else Report("the card Mekk-Knight Purple Nightfall", false);
            // Pin with a right-click (well, its handler), unpin with P's toggle.
            _view.ClosePhasePicker();
            _view.PinCard(pinInfo);
            yield return new WaitForSecondsRealtime(0.2f);
            if (_cardPack.Get(luster) is { } other) _view.ShowInfo(other);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("pinned");
            Report($"a right-click pins the card, and pointing at another shows it beside ({_view.PinState.pinned} / {_view.PinState.hovered})",
                _view.PinState.pinned == pinInfo.Name && _view.PinState.hovered == "Luster Dragon");
            _view.TogglePin();
            Report("P (or a second right-click) unpins it", _view.PinState.pinned == null);
            bool ended = _view.GoToPhase("end");
            Report("End from the bar ends the turn", ended);
            // A story scene over the duel holds it (during the CPU's turn here): nothing moves until it's read.
            yield return new WaitForSecondsRealtime(0.3f);
            var storyProfile = Ygo.Engine.Campaign.Profile.New(_library.Load("classic"), _cardPack!, null);
            var scenes = _story.Pending(storyProfile, new[] { "start" });
            if (scenes.Count > 0)
            {
                bool storyDone = false;
                _storyScreen.Play(scenes, storyProfile, null, () => storyDone = true);
                int ticksAtStory = _ticks;
                yield return new WaitForSecondsRealtime(1.5f);
                Report($"a story scene over the duel holds it ({_ticks - ticksAtStory} steps while it was open)", _storyScreen.IsOpen && _ticks == ticksAtStory);
                _storyScreen.DebugFinish();
                yield return new WaitForSecondsRealtime(0.5f);
                Report($"and the duel goes on once it's read ({_ticks - ticksAtStory} steps)", storyDone && _ticks > ticksAtStory);
            }
            else Report("a story scene to open", false);

            // Turn 3: Battle, an attack (pointing at a target first), then Main 2 and End.
            yield return WaitForMain(Phase.Main1, 90);
            Report($"your next turn ({_board.Turn})", _board.Turn >= 3 && _board.TurnPlayer == 0);
            _view.OpenPhasePicker();
            yield return new WaitForSecondsRealtime(0.3f);
            lit = _view.DebugPhasePicker();
            yield return Shot("picker-turn3");
            Report($"Main Phase 1 with monsters: Battle, Main 2 and End ({string.Join(", ", lit)})", lit.SequenceEqual(new[] { "Battle", "Main 2", "End" }));
            _view.GoToPhase("battle");
            var until = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < until && !(_view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.BattlePrompt)) { AnswerOthers(); yield return null; }
            var battle = _view.Prompt.Current;
            int attack = battle?.Choices.FindIndex(c => c.Verb == "Attack") ?? -1;
            Report("the Battle Phase, with an attack to declare", attack >= 0);
            if (attack >= 0)
            {
                _view.Prompt.ClickChoice(attack);
                until = Time.realtimeSinceStartup + 10;
                while (Time.realtimeSinceStartup < until && _view.Prompt.Current?.Title.StartsWith("Select the attack target") != true) yield return null;
                var targets = _view.Prompt.Current;
                if (targets?.Title.StartsWith("Select the attack target") == true && targets.Choices.FirstOrDefault(c => c.Card != null)?.Card is { } target)
                {
                    _view.ShowAttackArrow(battle!.Choices[attack].Card!.Value, target, preview: true);
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Shot("arrow-preview");
                    var state = _view.AttackArrowState;
                    Report($"pointing at a target draws the arrow with the comparison ({state.readout})", state.showing && state.preview && state.readout.Contains(" vs "));
                    // Check Field: hold Space and the target banner goes so you can see the board; let go and it's back.
                    _view.DebugHoldCheckField(true);
                    yield return null;
                    yield return Shot("check-field");
                    Report($"holding Space (Check Field) hides the {_view.Dialogs.Kind} banner over the board",
                        _view.CheckingField && _view.Dialogs.Root != null && _view.OverlaysHidden);
                    _view.DebugHoldCheckField(false);
                    yield return null;
                    Report("letting go brings it back", !_view.CheckingField && !_view.OverlaysHidden);
                    _view.ToggleCheckField();
                    yield return null;
                    bool stayed = _view.CheckingField && _view.OverlaysHidden;
                    _view.ToggleCheckField();
                    yield return null;
                    Report("the banner's ▼ keeps it hidden until you come back", stayed && !_view.OverlaysHidden);
                    // The attack at a normal speed (at the top speed animations, the arrow included, are switched off).
                    speedIndex = 3;
                    UpdateControls();
                    // A real click leaves its button selected, and the button is destroyed with the prompt: the board
                    // must keep animating with a destroyed object selected (it once froze until another click).
                    var clicked = new GameObject("ClickedButton");
                    UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(clicked);
                    Destroy(clicked);
                    _view.Prompt.ClickChoice(targets.Choices.FindIndex(c => c.Card != null));
                    // The declared attack: its arrow, until the attack is over.
                    until = Time.realtimeSinceStartup + 6;
                    while (Time.realtimeSinceStartup < until && !(_view.AttackArrowState.showing && !_view.AttackArrowState.preview)) { AnswerOthers(); yield return null; }
                    yield return Shot("arrow-attack");
                    Report($"the declared attack shows its arrow ({_view.AttackArrowState.readout})", _view.AttackArrowState.showing && !_view.AttackArrowState.preview);
                    until = Time.realtimeSinceStartup + 15;
                    while (Time.realtimeSinceStartup < until && _view.AttackArrowState.showing) { AnswerOthers(); yield return null; }
                    Report("and it goes away when the attack is over", !_view.AttackArrowState.showing);
                    Report($"(that attack was cut short: Dimensional Prison banished the attacker, {_board.Players[0].Banished.Count} banished)",
                        _board.Players[0].Banished.Count > 0);
                    speedIndex = Speeds.Length - 1;
                    UpdateControls();
                }
                else Report("the attack target choice", false);
            }
            // Main 2 from the Battle Phase, then End (declining anything offered on the way, like MST after the attack).
            until = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < until && !(_view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.BattlePrompt)) { AnswerOthers(); yield return null; }
            _view.GoToPhase("main2");
            yield return WaitForMain(Phase.Main2, 20);
            Report($"Main 2 from the Battle Phase ({Phase.Name(_board.Phase)})", _board.Phase == Phase.Main2);
            _view.GoToPhase("end");

            // Skip to my turn: the CPU's turn 4 at once (answering anything it stops for), then a recap of it.
            until = Time.realtimeSinceStartup + 15;
            while (Time.realtimeSinceStartup < until && !(_board.TurnPlayer == 1 && CanSkipTurn())) yield return null;
            yield return null;
            bool offered = _view.SkipOffered;
            yield return Shot("skip-button");
            // Their turn: reading a Mekk-Knight still shows the columns, but no "summon here" (you can't Summon now).
            if (_cardPack!.All.FirstOrDefault(c => c.Name == "Mekk-Knight Purple Nightfall") is { } nightfall2)
            {
                bool wasPaused = _paused;
                _paused = true;
                _view.ShowInfo(nightfall2);
                yield return null;
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("columns-their-turn");
                var theirs = _view.ColumnGuide;
                Report($"on their turn ({Phase.Name(_board.Phase)}) the columns show without \"summon here\" ({string.Join(" | ", _view.ColumnLabels)})",
                    _board.TurnPlayer == 1 && theirs.card == nightfall2.Name && _view.ColumnLabels.Count == 5 && !_view.ColumnLabels.Any(c => c.Contains("summon here"))
                    && theirs.ready.Zip(_view.ColumnLabels, (r, c) => !r || c.EndsWith("✓")).All(x => x));
                _view.ShowInfo(_cardPack.Get(blueEyes)!);
                yield return null;
                _paused = wasPaused;
            }
            _view.RequestSkip();
            var skipStart = Time.realtimeSinceStartup;
            until = skipStart + 60;
            while (Time.realtimeSinceStartup < until && (_skipping || _board.TurnPlayer != 0))
            {
                if (_runner != null && _runner.IsWaiting && _board.TurnPlayer != 0) AnswerOthers();
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("skip-recap");
            Report($"Skip to my turn plays their turn at once ({Time.realtimeSinceStartup - skipStart - 0.3f:0.0} s, {SkipState.steps} steps) and lists it: "
                   + string.Join(" | ", _view.SkipRecap ?? new List<string>()), offered && !_skipping && _board.TurnPlayer == 0 && _view.SkipRecap != null);
            Report("and the button is gone on your turn", !_view.SkipOffered);
            _view.CloseSkipRecap();

            // Turn 5: Main 2 straight from Main 1 (through the Battle Phase without stopping).
            yield return WaitForMain(Phase.Main1, 90);
            if (_board.TurnPlayer == 0 && _board.Winner < 0)
            {
                bool asked = _view.GoToPhase("main2");
                until = Time.realtimeSinceStartup + 20;
                while (Time.realtimeSinceStartup < until && !(_view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.IdlePrompt && _board.Phase == Phase.Main2))
                {
                    var set = _view.Prompt.Current;
                    if (set != null && set.IsChain && set.CanCancel) _view.Prompt.CancelSelection();
                    yield return null;
                }
                yield return Shot("main2");
                Report($"Main 2 straight from Main 1 ({Phase.Name(_board.Phase)})", asked && _board.Phase == Phase.Main2);
            }
            else Report($"the duel reached turn 5 for the Main 1 to Main 2 jump (turn {_board.Turn}, winner {_board.Winner})", false);

            // Conceding: your deck asks first; Keep playing (or Esc) backs out; Concede ends the duel as your loss.
            if (_board.Winner < 0)
            {
                _view.PinCard(pinInfo);
                _view.DebugDeckClick();
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("concede");
                Report("clicking your deck asks \"Concede the duel?\"", _view.ConcedeOpen);
                _view.CloseOverlays();
                Report("Keep playing (or Esc) closes it, and the duel goes on", !_view.ConcedeOpen && _board.Winner < 0);
                _view.DebugDeckClick();
                _view.DebugConfirmConcede();
                until = Time.realtimeSinceStartup + 10;
                while (Time.realtimeSinceStartup < until && !_announcedResult) yield return null;
                Report($"Concede ends the duel at once as your loss (winner {_board.Winner})", _announcedResult && _board.Winner == 1);
                // Its replay ends the same way.
                var conceded = _recording;
                if (conceded != null)
                {
                    WatchReplay(conceded, 0);
                    until = Time.realtimeSinceStartup + 60;
                    while (Time.realtimeSinceStartup < until && !_announcedResult) yield return null;
                    Report($"its replay ends with the concession too (winner {_board.Winner})", _announcedResult && _board.Winner == 1);
                }
                else Report("the conceded duel was recorded", false);
                // The next duel starts with nothing pinned.
                seed++;
                NewDuel();
                yield return new WaitForSecondsRealtime(0.5f);
                Report($"a new duel starts with nothing pinned ({_view.PinState.pinned ?? "none"})", _view.PinState.pinned == null);
            }
            // Skip to my turn into decisions of yours: a chance to chain, a card to pick and a zone (DuelController.SkipCheck).
            yield return SkipDecisionsCheck(Report, Shot);
            Debug.Log(bad == 0 ? "controls check: all passed" : $"controls check: {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private System.Collections.IEnumerator PileCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"pile-{name}.png"));
                yield return null;
                yield return null;
            }
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"pile check: {(ok ? "ok  " : "FAIL")} {what}"); }
            yield return new WaitForSecondsRealtime(0.5f);
            const uint aleister = 86120751, blueSky = 20537097, glowUpBulb = 67441435, maxxC = 23434538, luster = 11091375, kuriboh = 40640057;
            // You (Mekk-Knight Invoked): two monsters (Link material), Glow-Up Bulb in the GY (it summons itself from there),
            // a card banished face-down. The opponent: two cards in their GY.
            _checkSetup = new List<PlacedCard>
            {
                new PlacedCard(0, aleister, Location.MZone, 1, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(0, blueSky, Location.MZone, 3, Ygo.Engine.Position.FaceUpAttack),
                new PlacedCard(0, glowUpBulb, Location.Grave),
                new PlacedCard(0, maxxC, Location.Removed, 0, Ygo.Engine.Position.FaceDownAttack),
                new PlacedCard(1, luster, Location.Grave),
                new PlacedCard(1, kuriboh, Location.Grave),
            };
            _pickCharacter[0] = -1;
            _pickDeck[0] = "mekk-knight-invoked";
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == "rookie-ryo"));
            _pickDeck[1] = "classic";
            seed = 11;
            _paused = false;
            NewDuel();
            UpdateControls();
            var deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline && !(_view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.IdlePrompt)) yield return null;
            Report("your Main Phase with the set-up board", _view.Prompt.Current?.Prompt is Ygo.Engine.Protocol.IdlePrompt);
            yield return new WaitForSecondsRealtime(0.5f);

            var menu = _view.DebugPileClick(0, Location.Extra);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("extra-menu");
            Report($"a click on your Extra Deck asks: {string.Join(", ", menu)}", menu.Contains("View") && menu.Contains("Summon") && menu.Contains("Cancel"));
            var picker = _view.DebugPress("Summon");
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("extra-picker");
            Report($"Summon shows the Extra Deck monsters you can summon ({string.Join(", ", picker)})", picker.Count == 1 && picker[0].StartsWith("picker") && picker[0] != "picker 0");
            var before = _view.Prompt.Current;
            var after = _view.DebugPress("Cancel");
            yield return new WaitForSecondsRealtime(0.2f);
            Report("Cancel closes it and you're back where you were", after.Count == 0 && _view.Prompt.Current == before && before != null);

            menu = _view.DebugPileClick(0, Location.Grave);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("gy-menu");
            Report($"a click on your Graveyard asks: {string.Join(", ", menu)}", menu.Contains("View") && menu.Contains("Activate") && menu.Contains("Cancel"));
            var verb = "Activate";
            picker = _view.DebugPress(verb);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("gy-picker");
            Report($"{verb} shows the Graveyard cards you can use ({string.Join(", ", picker)})", picker.Count == 1 && picker[0] == "picker 1");
            _view.DebugPress("Cancel");
            yield return new WaitForSecondsRealtime(0.2f);

            // A double-click on a pile you can use opens the viewer (the menu's second click), and on the opponent's GY.
            _view.DebugPileClick(0, Location.Extra);
            var viewer = _view.DebugSecondClick(0, Location.Extra);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("extra-viewer");
            Report($"a double-click on your Extra Deck shows its cards ({string.Join(", ", viewer)})", viewer.Count == 1 && viewer[0] == "viewer 15");
            _view.CloseOverlays();
            _view.DebugPileClick(1, Location.Grave);
            viewer = _view.DebugSecondClick(1, Location.Grave);
            yield return new WaitForSecondsRealtime(0.2f);
            Report($"a double-click on the opponent's Graveyard shows it and stays open ({string.Join(", ", viewer)})", viewer.Count == 1 && viewer[0] == "viewer 2");
            _view.CloseOverlays();
            viewer = _view.DebugPileClick(0, Location.Removed);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("banished");
            Report($"your banished cards show, but a face-down one stays face-down ({string.Join(", ", viewer)}, {_view.DebugViewerFaceUp()} face-up)",
                viewer.Count == 1 && viewer[0] == "viewer 1" && _view.DebugViewerFaceUp() == 0);
            _view.CloseOverlays();
            viewer = _view.DebugPileClick(1, Location.Extra);
            Report("the opponent's Extra Deck stays hidden", viewer.Count == 0);

            // A card's own menu has Cancel.
            var hand = _board.Players[0].Hand;
            List<string> cardMenu = new List<string>();
            for (int i = 0; i < hand.Count && cardMenu.Count == 0; i++) cardMenu = _view.DebugClickCard(0, Location.Hand, i);
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("card-menu");
            Report($"a card's menu ends with Cancel ({string.Join(", ", cardMenu)})", cardMenu.Contains("Cancel"));
            bool closed = _view.CloseOverlays() && !_view.ActionMenuOpen;
            Report("Esc (or Cancel) closes it", closed);

            // Details: every card you may read offers it, and it opens the card reader over the duel.
            Report($"a hand card's menu offers Details ({string.Join(", ", cardMenu)})", cardMenu.Contains("Details"));
            int readSeq = -1;
            for (int i = 0; i < hand.Count && readSeq < 0; i++) if (hand[i] != null) readSeq = i;
            bool opened = readSeq >= 0 && _view.DebugReadCard(0, Location.Hand, readSeq);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("card-reader");
            var readName = readSeq >= 0 && hand[readSeq] is { } readCard ? _cardPack!.Get(readCard.Code)?.Name : null;
            var reader = _view.ReaderDetail;
            Report($"Details opens the card reader for {readName} with its text ({reader?.Text.Length ?? 0} letters, text column {reader?.TextWidth ?? 0:0} wide)",
                opened && reader?.Shown?.Name == readName && reader!.Text.Length > 0 && reader.TextWidth > 600);
            bool readerClosed = _view.CloseOverlays() && !_view.ReaderOpen;
            Report("Esc closes the card reader", readerClosed);

            // Effect callouts: one per activation, stacked top to bottom in order, each for three seconds; they fade in
            // the same order and the rest slide up. When the stack would cover the board, the oldest leave early.
            string NameOf(uint code) => _cardPack!.Get(code)?.Name ?? "";
            List<string> Stack() => _view.Callouts.Select(s => s.Split('|')[0]).ToList();
            uint pot = 55144522, raigeki = 12580477, ash = 14558127; // Pot of Greed, Raigeki, Ash Blossom & Joyous Spring
            _view.DebugCallout(pot, 0, 1);
            yield return new WaitForSecondsRealtime(0.5f);
            _view.DebugCallout(raigeki, 1, 2);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("callouts");
            var tops = _view.CalloutTops;
            float secondTop = tops.Count > 1 ? tops[1] : 0;
            Report($"each activation's callout stacks below the last ({string.Join(" / ", Stack())}, tops {string.Join(", ", tops.Select(t => t.ToString("0")))})",
                Stack().SequenceEqual(new[] { NameOf(pot), NameOf(raigeki) }) && tops.Count == 2 && tops[1] > tops[0] && !_view.CalloutsCoverBoard);
            // Pot of Greed was shown 0.9 s ago: it fades at 3 s and is gone by 3.35 s; Raigeki slides up.
            yield return new WaitForSecondsRealtime(2.75f);
            yield return Shot("callouts-fading");
            tops = _view.CalloutTops;
            Report($"the oldest leaves first and the rest slide up ({string.Join(" / ", Stack())}, top {(tops.Count > 0 ? tops[0] : -1):0} from {secondTop:0})",
                Stack().SequenceEqual(new[] { NameOf(raigeki) }) && tops[0] < secondTop - 20);
            yield return new WaitForSecondsRealtime(0.6f);

            // Room: three fit beside the board; one more long one sends the oldest off early, until nothing covers it.
            _view.DebugCallout(pot, 0, 1);
            yield return new WaitForSecondsRealtime(0.3f);
            _view.DebugCallout(raigeki, 1, 2);
            yield return new WaitForSecondsRealtime(0.3f);
            _view.DebugCallout(ash, 1, 3);
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("callouts-full");
            Report($"three fit beside the board without covering it ({string.Join(" / ", Stack())})",
                Stack().SequenceEqual(new[] { NameOf(pot), NameOf(raigeki), NameOf(ash) }) && !_view.CalloutsCoverBoard);
            _view.DebugCallout(ash, 0, 4);
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Shot("callouts-crowded");
            Report($"a long fourth one sends the oldest off early, Raigeki too, so none reach the board ({string.Join(" / ", Stack())})",
                !Stack().Contains(NameOf(pot)) && !Stack().Contains(NameOf(raigeki)) && Stack().LastOrDefault() == NameOf(ash) && !_view.CalloutsCoverBoard);
            yield return new WaitForSecondsRealtime(3.2f);
            Report($"and three seconds after the last, they're all gone ({_view.Callouts.Count} left)", _view.Callouts.Count == 0);

            // A long card text over a three-link chain (the 10-29 QA run's Lunalight Liger Dancer): the text makes way, so
            // the chain panel under it stays off your Banished pile.
            uint Code(string name) => _cardPack!.All.FirstOrDefault(c => c.Name == name && c.Alias == 0)?.Code ?? 0;
            uint liger = Code("Lunalight Liger Dancer"), mechaba = Code("Invoked Mechaba"), marten = Code("Lunalight Yellow Marten");
            _view.ShowChain(new[]
            {
                new Ygo.Engine.Presentation.TrackedLink { Number = 3, Code = liger, Controller = 1 },
                new Ygo.Engine.Presentation.TrackedLink { Number = 2, Code = mechaba, Controller = 0, State = Ygo.Engine.Presentation.LinkState.Waiting },
                new Ygo.Engine.Presentation.TrackedLink { Number = 1, Code = marten, Controller = 1, State = Ygo.Engine.Presentation.LinkState.Waiting },
            });
            _view.DebugCallout(liger, 1, 3);
            yield return new WaitForSecondsRealtime(0.7f);
            yield return Shot("callouts-chain");
            Report($"a long callout over a three-link chain leaves the board clear ({string.Join(" / ", Stack())}, chain at {_view.ChainPanelTop:0})",
                liger != 0 && Stack().SequenceEqual(new[] { NameOf(liger) }) && !_view.CalloutsCoverBoard);
            _view.ShowChain(Array.Empty<Ygo.Engine.Presentation.TrackedLink>());
            Debug.Log(bad == 0 ? "pile check: all passed" : $"pile check: {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private System.Collections.IEnumerator LayoutCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            var small = new Dictionary<string, (int size, string shot)>();
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"layout-{name}.png"));
                foreach (var (label, size) in SmallTexts())
                    if (!small.ContainsKey(label)) small[label] = (size, name);
                yield return null;
                yield return null;
            }
            void Report(string what, bool ok) => Debug.Log($"layout check: {(ok ? "ok  " : "FAIL")} {what}");
            var rng = new System.Random(5);
            void RandomClick(ChoiceSet set)
            {
                if (set.CanCancel && rng.NextDouble() < 0.3) { _view.Prompt.CancelSelection(); return; }
                switch (set.Mode)
                {
                    case ChoiceMode.Multi:
                        foreach (var i in Enumerable.Range(0, set.Choices.Count).OrderBy(_ => rng.Next()).Take(Math.Max(set.Min, 1))) _view.Prompt.ClickChoice(i);
                        _view.Prompt.Confirm();
                        break;
                    case ChoiceMode.Incremental when set.CanFinish && rng.NextDouble() < 0.5:
                        _view.Prompt.FinishSelection();
                        break;
                    default:
                        var open = Enumerable.Range(0, set.Choices.Count).Where(i => !set.Choices[i].AlreadySelected).ToList();
                        _view.Prompt.ClickChoice(open.Count > 0 ? open[rng.Next(open.Count)] : 0);
                        break;
                }
            }
            yield return new WaitForSecondsRealtime(0.5f);
            Report("the play layout is the default", _view.PlayLayout);
            var (windowW, windowH) = WindowSize.Remembered();
            float ppp = WindowSize.PixelsPerPoint();
            Report($"screen {Screen.width}x{Screen.height} ({ppp:0.##} px per pt); going windowed opens at {windowW}x{windowH} px (the last window, else 80% of the display in 16:9), "
                   + $"at least {WindowSize.MinPointsW}x{WindowSize.MinPointsH} pt where the display has room",
                windowW >= Mathf.Min(WindowSize.MinPointsW * ppp, Screen.currentResolution.width) - 2 && windowH >= Mathf.Min(WindowSize.MinPointsH * ppp, Screen.currentResolution.height) - 2);
            // Tall windows: the layout centred with bars (Letterbox), a dim carried into them; none at 16:9. The window is put back after.
            {
                int width0 = Screen.width, height0 = Screen.height;
                var mode0 = Screen.fullScreenMode;
                var boxResults = new List<(string what, bool ok)>();
                try
                {
                    foreach (var (bw, bh) in new[] { (1280, 1024), (1920, 1080) })
                    {
                        Screen.SetResolution(bw, bh, FullScreenMode.Windowed);
                        yield return new WaitForSecondsRealtime(1.2f);
                        var probe = Ui.Overlay(transform, "LetterboxProbe", 60);
                        var dim = Ui.Stretched("Dim", probe, Vector2.zero, Vector2.one);
                        Ui.Image(dim, new Color(0, 0, 0, 0.6f)).raycastTarget = true;
                        yield return null;
                        yield return null;
                        yield return null;
                        var canvasRt = (RectTransform)probe.parent;
                        float canvasH = canvasRt.rect.height, bar = Letterbox.Bar(probe);
                        float expected = Mathf.Max(0, (canvasH - 1080) / 2);
                        bool centred = Mathf.Abs(probe.offsetMin.y + probe.offsetMax.y) < 0.01f && Mathf.Abs(probe.offsetMin.y - bar) < 0.01f;
                        var top = Letterbox.TopFill(dim);
                        // Not through Shot: the board under the probe stays out of the play layout's text-size notes.
                        ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"layout-letterbox-{bw}x{bh}.png"));
                        yield return null;
                        yield return null;
                        if (bh * 16 > bw * 9 + 1)
                            boxResults.Add(($"{Screen.width}x{Screen.height}: bar {bar:0.#} (canvas {canvasH:0}, expected about {expected:0.#}), centred {centred}, dim's top strip {(top != null ? $"shown, blocks clicks {top.raycastTarget}" : "missing")}",
                                Mathf.Abs(bar - expected) <= 1.5f && bar > 0 && centred && top != null && top.raycastTarget));
                        else
                            boxResults.Add(($"{Screen.width}x{Screen.height}: bar {bar:0.#}, no strips ({top == null})", bar == 0 && top == null));
                        Destroy(probe.parent.gameObject);
                        yield return null;
                    }
                }
                finally
                {
                    // Every way out of this step (an exception included) puts the window back.
                    Screen.SetResolution(width0, height0, mode0);
                }
                yield return new WaitForSecondsRealtime(1.0f);
                Report($"tall windows letterbox the layout, wide ones don't: {string.Join("; ", boxResults.Select(r => r.what))}", boxResults.Count == 2 && boxResults.All(r => r.ok));
            }

            // Free Duel setup: you with Snake-Eye against Dante's Burning Abyss.
            _pickCharacter[0] = -1;
            _pickDeck[0] = "snake-eye";
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Decks.Contains("burning-abyss")));
            _pickDeck[1] = "burning-abyss";
            ShowFreeDuel();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("free-duel");
            Report("the Free Duel screen offers Duel!, a random opponent and Back", _scenes.Buttons.ContainsKey("Duel!") && _scenes.Buttons.ContainsKey("Random opponent"));
            _scenes.Buttons["Duel!"]();
            speedIndex = 5;
            UpdateControls();
            var deadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < deadline && (_view.Prompt.Current == null || _runner == null || !_runner.IsWaiting)) yield return null;
            Report($"Duel! starts a Free Duel in the play layout ({System.Text.RegularExpressions.Regex.Replace(_view.HeaderTitle, "<[^>]+>", "")})", _view.PlayLayout && _human != null);
            deadline = Time.realtimeSinceStartup + 6;
            while (_view.DetailVisible && Time.realtimeSinceStartup < deadline) yield return null;
            yield return Shot("board");
            Report("with no card looked at, the card detail is out of the way", !_view.DetailVisible);
            // The playable-card tabs, on and then off (an override for this run: the saved setting isn't touched).
            int Tabs() => FindObjectsByType<RectTransform>(FindObjectsSortMode.None).Count(t => t.name == "Act" && t.gameObject.activeInHierarchy);
            var savedTabs = FxSettings.PlayableTabsOverride;
            FxSettings.PlayableTabsOverride = true;
            _view.Render(_board);
            yield return null;
            int withTabs = Tabs();
            Report($"cards you can play show a ▶ tab ({withTabs})", withTabs > 0);
            FxSettings.PlayableTabsOverride = false;
            _view.Render(_board);
            yield return null;
            yield return Shot("board-no-tabs");
            Report($"with Playable card tabs off, none show ({Tabs()})", Tabs() == 0);
            FxSettings.PlayableTabsOverride = savedTabs;
            _view.Render(_board);
            if (_cardPack!.Get(89631139) is { } shown) _view.ShowInfo(shown);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("detail");
            Report("a card the duel shows slides the detail in", _view.DetailVisible);
            Report($"its text has room to read ({_view.Detail.TextWidth:0} wide)", _view.Detail.TextWidth >= 200);
            // Settings from the gear mid-duel, then Back: the duel carries on (it used to stay paused, stuck on "Resolving...").
            {
                bool wasPaused = _paused;
                _view.DebugOpenSettings();
                yield return new WaitForSecondsRealtime(0.2f);
                bool pausedInSettings = _paused;
                _settings.Back();
                yield return new WaitForSecondsRealtime(0.2f);
                Report($"Settings from the gear pauses the duel and Back resumes it (paused in Settings: {pausedInSettings}, after: {_paused})",
                    pausedInSettings && _paused == wasPaused && !_paused);
            }
            // Card term help: Kozmo Dark Destroyer's terms in blue; pointing at one shows what it means.
            if (_cardPack!.Get(55885348) is { } kozmo)
            {
                _view.ShowInfo(kozmo);
                yield return new WaitForSecondsRealtime(0.3f);
                int terms = _view.Detail.TermCount;
                var (shownTerm, expectedTerm) = _view.Detail.DebugHoverTerm(1);
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("glossary");
                Report($"card terms are marked ({terms} in Kozmo Dark Destroyer) and pointing at one explains it ({shownTerm})",
                    terms >= 3 && shownTerm != null && shownTerm == expectedTerm);
                TermTooltip.Hide();
            }
            // Pinned: a long Spell pinned, another card hovered beside it; both panels keep a readable text column.
            if (_cardPack!.Get(35269904) is { } thrust) // Triple Tactics Thrust
            {
                _view.ShowInfo(thrust);
                _view.TogglePin();
                if (_cardPack.Get(89631139) is { } hovered) _view.ShowInfo(hovered);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("pinned");
                Report($"pinned and compared, both texts have room ({_view.Detail.TextWidth:0} and {_view.HoverDetail.TextWidth:0} wide)",
                    _view.Detail.TextWidth >= 200 && _view.HoverDetail.TextWidth >= 200);
                _view.TogglePin();
            }
            BoardView.DebugIgnorePointer = true; // (a mouse left over the hand would hold it open)
            yield return new WaitForSecondsRealtime(4.2f);
            Report("and it slides away again after a few seconds", !_view.DetailVisible);
            BoardView.DebugIgnorePointer = false;
            _view.ToggleGear();
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("gear");
            var gear = _view.GearRows();
            Report($"the gear holds the duel's settings ({string.Join(", ", gear)})", gear.Contains("Speed") && gear.Contains("Chain prompts") && gear.Contains("Text log") && gear.Contains("Settings"));
            _view.ToggleGear();

            // Play a few turns with random clicks: the history fills in, and the opponent's plays get a message.
            bool toastShot = false;
            deadline = Time.realtimeSinceStartup + 90;
            while (Time.realtimeSinceStartup < deadline && !_announcedResult && _board.Turn < 4)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                if (!toastShot && (_view.ToastText.Length > 0 || _view.OpponentCallout.Length > 0))
                {
                    toastShot = true;
                    yield return Shot("message");
                    Report($"a message calls out the opponent's play (\"{(_view.ToastText.Length > 0 ? _view.ToastText : _view.OpponentCallout)}\")", true);
                }
                var set = _view.Prompt.Current;
                if (set != null && _runner != null && _runner.IsWaiting) RandomClick(set);
            }
            if (!toastShot) Report("no message by turn 4 (the opponent didn't activate anything)", _history?.Entries.All(e => e.Kind != Ygo.Engine.Presentation.HistoryKind.Activate || e.Player == 0) == true);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("strip");
            Report($"the recent-actions strip shows {_view.StripTiles} tiles after {_history?.Entries.Count} history entries", _view.StripTiles > 0);
            _view.ToggleHistory();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("history");
            Report($"History opens the drawer ({_view.DrawerState.rows} rows)", _view.DrawerState.open && _view.DrawerState.rows > 0);
            _view.ToggleHistory();
            _view.ToggleLog();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("text-log");
            Report("the text log opens from the gear", _view.LogOpen);
            _view.ToggleLog();

            // The duelist panels (paused, so the duel doesn't redraw them): a hit shows its amount and reason beside the
            // LP; at 1,000 or less the tag says "! Low", the bar is red and only part of one segment is lit.
            bool pausedBefore = _paused;
            _paused = true;
            UpdateControls();
            yield return new WaitForSecondsRealtime(0.4f);
            int realLp = _board.Players[0].LifePoints;
            _view.DebugLifePointChange(0, realLp - 2500, Ygo.Engine.LpChange.Battle);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("panel-hit");
            var (litHit, _) = _view.LifePointBar(0);
            Report($"a hit shows its amount beside your LP ({_view.ChangeChip(0)}), and the bar counts thousands ({string.Join(" ", litHit.Select(f => f.ToString("0.#")))})",
                _view.ChangeChip(0) == $"−2500" && litHit.Count(f => f > 0.99f) == (realLp - 2500) / 1000);
            _view.DebugLifePointChange(0, 900, Ygo.Engine.LpChange.Cost);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return Shot("panel-low");
            var (litLow, barColor) = _view.LifePointBar(0);
            Report($"at 900 LP the tag reads \"{_view.LifePointTag(0)}\", the bar is red and one segment is 0.9 lit ({string.Join(" ", litLow.Select(f => f.ToString("0.#")))})",
                _view.LifePointTag(0) == "! Low" && barColor.r > 0.9f && barColor.g < 0.4f && Mathf.Abs(litLow[0] - 0.9f) < 0.02f && litLow.Skip(1).All(f => f == 0));
            Report($"the turn player's panel is lit (panel {_view.ActivePanel}, turn player {_board.TurnPlayer})", _view.ActivePanel == _board.TurnPlayer);
            _view.DebugLifePointChange(0, realLp, Ygo.Engine.LpChange.None);
            // The chain panel (QA 2026-11-01: a two-target line ended on a comma, the second target cut off; rows showed
            // through one another): a two-link chain whose first link targets two cards with long names, its newest link
            // with a long effect line, over that link's callout. Every row reads whole and stays in its place.
            {
                uint Code(string name) => _cardPack!.All.FirstOrDefault(c => c.Name == name && c.Alias == 0)?.Code ?? 0;
                uint twisters = Code("Twin Twisters"), ash = Code("Ash Blossom & Joyous Spring");
                uint first = Code("Mekk-Knight Crusadia Avramax"), second = Code("Elemental HERO Phoenix Enforcer");
                _view.ShowChain(new[]
                {
                    new Ygo.Engine.Presentation.TrackedLink { Number = 2, Code = ash, Controller = 1, State = Ygo.Engine.Presentation.LinkState.Activating },
                    new Ygo.Engine.Presentation.TrackedLink
                    {
                        Number = 1, Code = twisters, Controller = 0, State = Ygo.Engine.Presentation.LinkState.Waiting,
                        Targets = { new Ygo.Engine.Protocol.CardRef(first, 1, Location.MZone, 5), new Ygo.Engine.Protocol.CardRef(second, 1, Location.MZone, 2) },
                    },
                });
                _view.DebugCallout(ash, 1, 2);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("chain-panel");
                var chainLines = _view.ChainLines;
                var chainProblems = _view.ChainProblems;
                string N(uint c) => _cardPack!.Get(c)?.Name ?? "?";
                Report($"a two-link chain reads whole: {string.Join(" / ", _view.ChainRows)}; lines \"{string.Join("\" / \"", chainLines)}\"{(chainProblems.Count > 0 ? "; " + string.Join("; ", chainProblems) : "")}",
                    twisters != 0 && ash != 0 && first != 0 && second != 0 && chainProblems.Count == 0 && chainLines.Count == 2
                    && chainLines[1] == $"→ {N(first)}, {N(second)}" && chainLines[0].Length > 0);
                // Too many targets for two lines, one with a comma in its name ("Lilith, Lady of Lament"): the line keeps whole
                // names and counts the rest ("+N more"), the comma name as one.
                uint lilith = Code("Lilith, Lady of Lament");
                var many = new[] { lilith, first, second, first };
                var link = new Ygo.Engine.Presentation.TrackedLink { Number = 1, Code = twisters, Controller = 0, State = Ygo.Engine.Presentation.LinkState.Waiting };
                for (int t = 0; t < many.Length; t++) link.Targets.Add(new Ygo.Engine.Protocol.CardRef(many[t], 1, Location.MZone, (uint)t));
                _view.ShowChain(new[] { link });
                yield return null;
                var manyLine = _view.ChainLines.FirstOrDefault() ?? "";
                var manyProblems = _view.ChainProblems;
                bool wholeNames = Enumerable.Range(1, many.Length - 1)
                    .Any(k => manyLine == "→ " + string.Join(", ", many.Take(k).Select(N)) + $" +{many.Length - k} more");
                yield return Shot("chain-panel-many");
                Report($"four targets, one named with a comma, keep whole names and count the rest: \"{manyLine}\"{(manyProblems.Count > 0 ? "; " + string.Join("; ", manyProblems) : "")}",
                    lilith != 0 && wholeNames && manyProblems.Count == 0);
                _view.ShowChain(Array.Empty<Ygo.Engine.Presentation.TrackedLink>());
            }
            _paused = pausedBefore;
            UpdateControls();
            ToggleDevPanel();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("dev-panel");
            Report("F12 shows the developer panel (developer mode, as every check runs)", !_view.PlayLayout && DeveloperMode);
            Report($"in a Free Duel its tools are all on ({_view.DuelToolsState.on} on)", !_view.DuelToolsState.locked && _view.DuelToolsState.on >= 10);
            ToggleDevPanel();
            Report("and hides it again", _view.PlayLayout);

            // To the end, quickly: the result banner says what's next.
            speedIndex = Speeds.Length - 1;
            UpdateControls();
            deadline = Time.realtimeSinceStartup + 150;
            while (Time.realtimeSinceStartup < deadline && !_announcedResult)
            {
                yield return new WaitForSecondsRealtime(0.02f);
                var set = _view.Prompt.Current;
                if (set != null && _runner != null && _runner.IsWaiting) RandomClick(set);
            }
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("result");
            var after = _view.ResultButtons;
            Report($"the result offers {string.Join(", ", after)}", after.Contains("Rematch") && after.Contains("New matchup") && after.Contains("Menu"));

            // A replay: the controls float bottom right.
            var recent = _replays.All().FirstOrDefault(e => e.Recent);
            if (recent != null)
            {
                speedIndex = 3;
                WatchReplay(recent.Replay, 0);
                _paused = false;
                UpdateControls();
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Shot("replay");
                Report("a replay plays in the play layout with its controls", _view.PlayLayout && _view.WatchBarShowing);
            }
            // Cards without a picture (QA 2026-11-01: "Polymerizatio n", the deck editor's limit badge over "Terraforming"): the
            // editor with every card in its text look keeps each name's words whole and starts the name under the badge.
            {
                var deckName = _library.Names.FirstOrDefault(n => n.IndexOf("mekk", StringComparison.OrdinalIgnoreCase) >= 0) ?? _library.Names.FirstOrDefault();
                _editor.Open(deckName);
                yield return new WaitForSecondsRealtime(0.6f);
                foreach (var query in new[] { "polymerization", "terraforming" })
                {
                    // Terraforming under a list that limits it, so its badge shows (the Free Duel's default list limits nothing).
                    _editor.DebugTextLook(true, query, query == "terraforming" ? "Terraforming" : null);
                    yield return new WaitForSecondsRealtime(0.5f);
                    // Not through Shot: the editor's own small labels stay out of the play layout's text-size notes.
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"layout-editor-names-{query}.png"));
                    yield return null;
                    yield return null;
                    var (problems, names, badges) = _editor.DebugNameProblems();
                    Report($"in the editor, {names} cards without a picture (search \"{query}\", deck {deckName}, {badges} with a limit badge) keep their words whole and clear of the badge"
                           + (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(6)) : ""), names >= 10 && problems.Count == 0 && (query != "terraforming" || badges > 0));
                    _editor.DebugTextLook(false);
                }
                _editor.Close();
                yield return new WaitForSecondsRealtime(0.3f);
            }
            // Hover: a test mouse over buttons in both themes, the gold frame and the arrows (DuelController.HoverCheck).
            yield return HoverCheck(folder, Report);
            // A warning, not a check: text drawn under the 14 px the accessibility review asked for (card faces aside).
            Debug.Log($"layout check: note {small.Count} label{(small.Count == 1 ? "" : "s")} under {MinTextSize} px");
            foreach (var (label, (size, shot)) in small.OrderBy(s => s.Value.size).ThenBy(s => s.Value.shot))
                Debug.Log($"layout check: note   {size} px on {shot}: {label}");
            yield return new WaitForSecondsRealtime(0.3f);
            Application.Quit();
        }

        /// <summary>The smallest text size the accessibility review asked for (a layout check warning, not a rule).</summary>
        private const int MinTextSize = 14;

        /// <summary>
        /// The visible labels drawn smaller than <see cref="MinTextSize"/> (best-fit text at the size it came out), as
        /// "Parent/Label: text" with the size. Card faces are left out: they're drawn at card scale on purpose.
        /// </summary>
        private static List<(string label, int size)> SmallTexts()
        {
            var found = new List<(string, int)>();
            Canvas.ForceUpdateCanvases(); // so best-fit text reports the size it came out at this frame
            foreach (var text in FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None))
            {
                if (!text.isActiveAndEnabled || text.color.a < 0.1f || string.IsNullOrWhiteSpace(text.text)) continue;
                if (text.GetComponentInParent<CardView>() != null) continue;
                int size = text.resizeTextForBestFit && text.cachedTextGenerator.fontSizeUsedForBestFit > 0
                    ? text.cachedTextGenerator.fontSizeUsedForBestFit : text.fontSize;
                if (size >= MinTextSize) continue;
                var words = System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "").Replace('\n', ' ');
                if (words.Length > 40) words = words.Substring(0, 40) + "…";
                var parent = text.transform.parent != null ? text.transform.parent.name + "/" : "";
                found.Add(($"{parent}{text.name}: \"{words}\"", size));
            }
            return found;
        }

        private System.Collections.IEnumerator AutoClick()
        {
            var rng = new System.Random(seed);
            while (true)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                var set = _view.Prompt.Current;
                if (set == null || _runner == null || !_runner.IsWaiting) continue;
                if (set.CanCancel && rng.NextDouble() < 0.3) { _view.Prompt.CancelSelection(); continue; }
                switch (set.Mode)
                {
                    case ChoiceMode.Multi:
                        foreach (var i in Enumerable.Range(0, set.Choices.Count).OrderBy(_ => rng.Next()).Take(Math.Max(set.Min, 1)))
                            _view.Prompt.ClickChoice(i);
                        _view.Prompt.Confirm();
                        break;
                    case ChoiceMode.Incremental when set.CanFinish && rng.NextDouble() < 0.5:
                        _view.Prompt.FinishSelection();
                        break;
                    default:
                        var open = Enumerable.Range(0, set.Choices.Count).Where(i => !set.Choices[i].AlreadySelected).ToList();
                        _view.Prompt.ClickChoice(open.Count > 0 ? open[rng.Next(open.Count)] : 0);
                        break;
                }
            }
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary>
        /// Automated controller check (with -editor open): plugs in a simulated gamepad and drives the deck editor
        /// with it the way a player would, logging each result to Player.log.
        /// </summary>
        private System.Collections.IEnumerator ControllerTest()
        {
            for (int i = 0; i < 5; i++) yield return null; // let the editor lay out
            var pad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("TestPad");
            void Hold(UnityEngine.InputSystem.LowLevel.GamepadButton? button) =>
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad, button == null
                    ? new UnityEngine.InputSystem.LowLevel.GamepadState()
                    : new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(button.Value));
            System.Collections.IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
            System.Collections.IEnumerator Press(UnityEngine.InputSystem.LowLevel.GamepadButton button)
            {
                Hold(button);
                yield return Frames(3);
                Hold(null);
                yield return Frames(3);
            }
            void Report(string what, bool ok) => Debug.Log($"controller test: {(ok ? "ok  " : "FAIL")} {what}");
            int Home() => _editor.CardCount(DeckSection.Main) + _editor.CardCount(DeckSection.Extra);
            var A = UnityEngine.InputSystem.LowLevel.GamepadButton.South;
            var X = UnityEngine.InputSystem.LowLevel.GamepadButton.West;
            var B = UnityEngine.InputSystem.LowLevel.GamepadButton.East;

            int home = Home();
            _controller.DebugMoveTo(_editor.FirstResultOnScreen());
            yield return Frames(3);
            yield return Press(A);
            Report($"A on a search result adds it (main+extra {home}->{Home()})", Home() == home + 1);

            int side = _editor.CardCount(DeckSection.Side);
            _controller.DebugMoveTo(_editor.FirstResultOnScreen());
            yield return Frames(3);
            yield return Press(X);
            Report($"X on a search result adds it to the Side Deck (side {side}->{_editor.CardCount(DeckSection.Side)})", _editor.CardCount(DeckSection.Side) == side + 1);

            side = _editor.CardCount(DeckSection.Side);
            var from = _editor.FirstResultOnScreen();
            var to = _editor.SideZoneOnScreen();
            _controller.DebugMoveTo(from);
            yield return Frames(3);
            Hold(A);
            yield return Frames(3);
            for (int step = 1; step <= 12; step++)
            {
                _controller.DebugMoveTo(Vector2.Lerp(from, to, step / 12f));
                yield return null;
            }
            yield return Frames(2);
            Hold(null);
            yield return Frames(3);
            Report($"hold A and move drags a card to the Side Deck (side {side}->{_editor.CardCount(DeckSection.Side)})", _editor.CardCount(DeckSection.Side) == side + 1);

            var before = _editor.FirstResultOnScreen();
            _controller.DebugMoveTo(before);
            yield return Frames(2);
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.DpadLeft);
            var after = _controller.CursorPosition;
            Report($"d-pad left jumps the cursor left ({before.x:F0},{before.y:F0} -> {after.x:F0},{after.y:F0})",
                after.x < before.x - 10 && Mathf.Abs(after.y - before.y) <= before.x - after.x);

            int page = _editor.ResultsPage;
            yield return Press(UnityEngine.InputSystem.LowLevel.GamepadButton.RightShoulder);
            Report($"RB turns the results page ({page}->{_editor.ResultsPage})", _editor.ResultsPage == page + 1);

            _editor.DebugShowTestHand();
            yield return Frames(2);
            yield return Press(B);
            Report($"B closes the test hand (open: {_editor.TestHandOpen})", !_editor.TestHandOpen);
        }
#endif

        /// <summary>Two different characters, each with a deck from their own pool.</summary>
        /// <summary>
        /// Free play opens the first half of the campaign's characters (by ladder rank); the rest join as you beat them
        /// in the campaign. Characters outside the ladder are always open.
        /// </summary>
        private bool IsFreeCharacter(int rosterIndex)
        {
            var p = _roster[rosterIndex];
            // No Limits pilots play their peak builds only at No Limits and the Invitational (spec 4.3; review fix 4).
            if (IsNoLimitsPilot(p)) return false;
            var ranked = _roster.Count(c => c.Ladder > 0);
            if (p.Ladder == 0 || p.Ladder <= (ranked + 1) / 2) return true;
            return _campaign?.Profile?.HasBeaten(p.Id) == true;
        }

        /// <summary>Two bots (for attract mode), keeping the bottom seat a character rather than you.</summary>
        private void PickRandomBots()
        {
            if (_pickCharacter[0] < 0) _pickCharacter[0] = FreeCharacters()[0];
            PickRandom();
        }

        /// <summary>A No Limits pilot: every deck of theirs is a peak build or its Brawl deck (decks/no-limits, `nl-`).</summary>
        private static bool IsNoLimitsPilot(Personality p) => p.Decks.Count > 0 && p.Decks.All(d => d.StartsWith("nl-", StringComparison.Ordinal));

        private List<int> FreeCharacters() => Enumerable.Range(0, _roster.Count).Where(IsFreeCharacter).ToList();

        private void PickRandom()
        {
            var rng = new System.Random(Scramble(seed)); // not new Random(seed): neighboring seeds pick alike
            var free = FreeCharacters();
            if (_pickCharacter[0] < 0)
            {
                // Keep you in the bottom seat; just pick a new opponent and decks.
                _pickCharacter[1] = free[rng.Next(free.Count)];
                _pickDeck[0] = _allDecks[rng.Next(_allDecks.Count)];
                var pool = OwnDecks(_roster[_pickCharacter[1]]);
                _pickDeck[1] = pool[rng.Next(pool.Count)];
                ShowPicks();
                return;
            }
            _pickCharacter[0] = free[rng.Next(free.Count)];
            _pickCharacter[1] = free.Count > 1 ? free.Where(i => i != _pickCharacter[0]).ElementAt(rng.Next(free.Count - 1)) : _pickCharacter[0];
            for (int seat = 0; seat < 2; seat++)
            {
                var pool = OwnDecks(_roster[_pickCharacter[seat]]);
                _pickDeck[seat] = pool[rng.Next(pool.Count)];
            }
            ShowPicks();
        }

        private void ApplyCommandLinePicks()
        {
            var args = Environment.GetCommandLineArgs();
            string? Arg(string name)
            {
                var i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            for (int seat = 0; seat < 2; seat++)
            {
                var id = Arg($"-p{seat}");
                if (seat == 0 && id == "you")
                {
                    _pickCharacter[0] = -1;
                    _pickDeck[0] = DeckChoicesFor(0)[0];
                }
                var index = id == null ? -1 : _roster.FindIndex(p => p.Id == id);
                if (index >= 0)
                {
                    _pickCharacter[seat] = index;
                    _pickDeck[seat] = DeckChoices(_roster[index])[0];
                }
                var deck = Arg($"-deck{seat}");
                if (deck != null && _allDecks.Contains(deck)) _pickDeck[seat] = deck;
            }
            ShowPicks();
        }

        private List<string> OwnDecks(Personality p)
        {
            var own = p.Decks.Where(_allDecks.Contains).ToList();
            return own.Count > 0 ? own : _allDecks;
        }

        /// <summary>The character's own decks first, then every other deck.</summary>
        private List<string> DeckChoices(Personality p) =>
            OwnDecks(p).Concat(_allDecks.Where(d => !p.Decks.Contains(d))).ToList();

        private List<string> DeckChoicesFor(int seat) =>
            _pickCharacter[seat] < 0 ? _allDecks : DeckChoices(_roster[_pickCharacter[seat]]);

        /// <summary>
        /// A seat's duelist: the bottom seat cycles through "You" (index -1) and the characters open in free play; the
        /// top seat through those characters only.
        /// </summary>
        private void CycleCharacter(int seat, int delta)
        {
            int n = _roster.Count + 1;
            var pick = _pickCharacter[seat];
            for (int tries = 0; tries < n; tries++)
            {
                pick = (pick + 1 + delta + n) % n - 1;
                if ((pick < 0 && seat == 0) || (pick >= 0 && IsFreeCharacter(pick))) break;
            }
            _pickCharacter[seat] = pick;
            _pickDeck[seat] = DeckChoicesFor(seat)[0];
            ShowPicks();
        }

        private void CycleDeck(int seat, int delta)
        {
            var choices = DeckChoicesFor(seat);
            var i = Math.Max(0, choices.IndexOf(_pickDeck[seat]));
            _pickDeck[seat] = choices[(i + delta + choices.Count) % choices.Count];
            ShowPicks();
        }

        /// <summary>The Free Duel setup screen: each side's duelist and deck, then Duel! (or Watch).</summary>
        private void ShowFreeDuel()
        {
            ShowPicks(); // the developer panel's pickers follow
            CampaignScenes.Side Describe(int seat)
            {
                var deck = _library.Contains(_pickDeck[seat]) ? _library.Load(_pickDeck[seat]) : new Deck();
                var era = deck.Note("era");
                var side = new CampaignScenes.Side
                {
                    Deck = DeckTitle(_pickDeck[seat]), DeckNote = era.Length > 0 ? $"({era})" : "",
                    Showcase = CampaignScenes.ShowcaseOf(deck, _cardPack!),
                };
                if (_pickCharacter[seat] < 0)
                {
                    side.Name = "You";
                    side.Title = "You play this side";
                    side.Detail = "Your opponent's hand and set cards stay hidden.";
                    return side;
                }
                var p = _roster[_pickCharacter[seat]];
                side.Name = p.Name;
                side.Title = p.Title;
                side.Portraits.Add(p.Id);
                side.Detail = p.StyleSummary + (p.Decks.Contains(_pickDeck[seat]) ? "" : "\nNot one of their own decks");
                side.Quote = p.Intro;
                return side;
            }
            _scenes.ShowFreeDuel(Describe(0), Describe(1), _pickCharacter[0] < 0,
                character: (seat, delta) => { CycleCharacter(seat, delta); ShowFreeDuel(); },
                deck: (seat, delta) => { CycleDeck(seat, delta); ShowFreeDuel(); },
                random: () => { seed++; PickRandom(); ShowFreeDuel(); },
                start: () =>
                {
                    seed++;
                    CoinToss(Describe(0).Name, Describe(1).Name, _pickDeck[0], _pickDeck[1], () => { _paused = false; NewDuel(); UpdateControls(); },
                        youChoose: _pickCharacter[0] < 0);
                },
                back: () => _menu.Open());
        }

        private void ShowPicks()
        {
            for (int seat = 0; seat < 2; seat++)
            {
                if (_pickCharacter[seat] < 0)
                {
                    _view.SetPick(seat, "You", "You play this seat. Your opponent's hand stays hidden.", DeckTitle(_pickDeck[seat]));
                    continue;
                }
                var p = _roster[_pickCharacter[seat]];
                var deck = _pickDeck[seat];
                var label = p.Decks.Contains(deck) ? DeckTitle(deck) : $"{DeckTitle(deck)}*";
                _view.SetPick(seat, p.Name, $"{p.Title} · {p.StyleSummary}", label);
            }
        }

        /// <summary>You concede (from your deck's "Concede the duel?"): the duel ends now, as your loss.</summary>
        private void ConcedeDuel()
        {
            if (_view.CanConcede?.Invoke() != true) return;
            _runner!.Concede(0, DuelRunner.ReasonConceded); // your team is seat 0
            _view.ShowChoices(null, false);
            _paused = false;
            _answerReady = true; // step now, even in the middle of your own decision
        }

        /// <summary>
        /// A duel of yours ended (a win, a loss, a draw or a concession): the opponent's cards it showed go to the deck
        /// editor's Seen in duels (seen.txt), under the in-game character's name and the kind of duel.
        /// </summary>
        private void RecordSeen()
        {
            var seen = _seen;
            _seen = null; // once per duel
            if (seen == null || _watching != null) return;
            string mode = _campaignTag != null ? "campaign tag" : _campaignMatch != null ? "campaign"
                : _campaignCup != null || _campaignRound != null ? "library" : _guestDuel != null ? "guest"
                : _alleyTable != null ? "alley" : _ladderDuel != null ? "ladder" : _bracketMatch != null ? "bracket"
                : _tag != null ? "tag" : "free play";
            try { _editor.AddSeenDuel(TeamName(1), mode, seen); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Debug.LogWarning($"Could not save the cards seen: {ex.Message}"); }
        }

        private void AnnounceResult()
        {
            _view.HideAttackArrow(); // the winning blow's arrow: the attack never gets to end
            _announcedResult = true;
            ShowDuelOver();
            if (_runner?.Result.Reason == DuelRunner.ReasonYataLock && _board.Winner is 0 or 1)
                _view.AppendLog($"{TeamName(1 - _board.Winner)} {(_players[1 - _board.Winner].Id == "you" ? "are" : "is")} Yata-locked: no cards to play and no draws to come.");
            // Your result when you play; a fanfare for the winner when you watch.
            bool youPlay = _tag != null ? _tag.YouPlay : _pickCharacter[0] < 0;
            // A boss's theme has its own endings; otherwise the fanfare or the sad descent.
            if (_board.Winner > 1) Sounds.Play("turn");
            else if (!Sounds.Ending(win: _board.Winner == 0)) Sounds.Play(youPlay && _board.Winner != 0 ? "lose" : "win");
            if (_recording != null)
            {
                // Keep every finished duel in the recent replays.
                _recording.Winner = _board.Winner;
                _recording.Turns = _board.Turn;
                _recording.Date = DateTime.Now;
                try { _replays.SaveRecent(_recording); }
                catch (IOException ex) { Debug.LogWarning($"Could not save the replay: {ex.Message}"); }
            }
            RecordSeen();

            if (FinishShowDuel()) return;
            if (FinishGuestDuel()) return;
            if (FinishLadderDuel()) return;
            if (FinishAlleyDuel()) return;
            if (FinishBracketDuel()) return;
            if (_campaignTag is { } ct && _watching == null && _campaign.Profile != null)
            {
                var tagReward = _campaign.Circuits.RecordTag(_campaign.Profile, ct.cup, ct.match, _board.Winner > 1 ? 2 : _board.Winner,
                    loaner: TeamHasLoaner(_tag, ""));
                _campaign.EndLiveDuel();
                _campaign.AfterDuel(tagReward, "", _tally, TeamHasLoaner(_tag, ""), tag: true);
                _campaign.Save();
                BeginTagResults(ct.cup, ct.match, tagReward, _board.Winner, ct.partner);
                // A Yata-lock ending says who's locked (the team), as a round's or a cup's result does.
                var tagLine = _runner?.Result.Reason == DuelRunner.ReasonYataLock && _board.Winner is 0 or 1 ? YataLockLine(TeamName(1), _partners.Length > 0 ? _partners[0].Name : "your partner") : "";
                _view.SetResult(_board.Winner > 1 ? "Draw!" : _board.Winner == 0 ? "You win!" : $"{TeamName(1)} win!", tagLine, $"{tagReward.Summary}   ·   {_campaign.Profile.Dp:N0} DP");
                _view.AppendLog($"Campaign: {tagReward.Summary}.");
                return;
            }
            if (_campaignRound is int fr && _watching == null && _campaign.Profile != null)
            {
                // An "Every Era" round (the versus screen let only a legal deck of yours start it).
                var opponent = _campaign.Library.Rounds[fr].Match.Character;
                var reward = _campaign.Library.RecordFinal(_campaign.Profile, fr, _board.Winner > 1 ? 2 : _board.Winner, _pickDeck[0]);
                _campaign.EndLiveDuel();
                _campaign.AfterDuel(reward, "", _tally, _campaign.IsLoanerDeck(_pickDeck[0]), tag: false);
                _campaign.Save();
                BeginRoundResults(fr, reward, _board.Winner);
                var headline = _board.Winner > 1 ? "Draw!" : _board.Winner == 0 ? "You win!" : $"{opponent.Name} wins!";
                var line = _board.Winner == 0 ? opponent.LoseLine : _board.Winner == 1 ? opponent.WinLine : "";
                if (_runner?.Result.Reason == DuelRunner.ReasonYataLock) line = YataLockLine(opponent.Name);
                _view.SetResult(headline, line, $"{reward.Summary}   ·   {_campaign.Profile.Dp:N0} DP");
                if (line.Length > 0) _view.AppendLog($"{opponent.Name}: \"{line}\"");
                _view.AppendLog($"{_campaign.Library.Final.Name}: {reward.Summary}.");
                return;
            }
            if (_campaignCup is { } lc && _watching == null && _campaign.Profile != null)
            {
                // A Library cup: pay out (half with a loaner; the stamp counts a deck you built, with no Ban Passes), save, results.
                var opponent = _campaign.Library.Cups[lc.cup].Matches[lc.match].Character;
                bool lent = _campaign.IsLoanerDeck(_pickDeck[0]);
                // The stamp counts a deck you built (no loaner, no partner's edit: the final's rule too) with no Ban Passes; the
                // engraving skips a partner's edit the same way (the deck as saved, Loaners.IsYours; owner, 2026-11-05).
                var played = _library.Contains(_pickDeck[0]) ? _library.Load(_pickDeck[0]) : null;
                bool built = Library.CountsForStamp(played, played == null ? 0 : _campaign.PassesUsed(played, _campaign.CupRules(lc.cup)));
                var lendable = Loaners.NameFor(_campaign.Library.Cups[lc.cup].Matches[lc.match].Deck);
                bool couldLend = _campaign.LoanerDecks().Contains(lendable);
                var reward = _campaign.Library.Record(_campaign.Profile, lc.cup, lc.match, _board.Winner > 1 ? 2 : _board.Winner, loaner: lent, built: built, deck: _pickDeck[0], played: played);
                _campaign.EndLiveDuel();
                _campaign.AfterDuel(reward, "", _tally, lent, tag: false);
                _campaign.Save();
                bool newLoaner = !couldLend && _campaign.LoanerDecks().Contains(lendable);
                BeginCupResults(lc.cup, lc.match, reward, _board.Winner, lent, built, newLoaner);
                var headline = _board.Winner > 1 ? "Draw!" : _board.Winner == 0 ? "You win!" : $"{opponent.Name} wins!";
                var line = _board.Winner == 0 ? opponent.LoseLine : _board.Winner == 1 ? opponent.WinLine : "";
                if (_runner?.Result.Reason == DuelRunner.ReasonYataLock) line = YataLockLine(opponent.Name);
                _view.SetResult(headline, line, $"{reward.Summary}   ·   {_campaign.Profile.Dp:N0} DP");
                if (line.Length > 0) _view.AppendLog($"{opponent.Name}: \"{line}\"");
                _view.AppendLog($"Format Library: {reward.Summary}.");
                return;
            }
            if (_campaignMatch is { } cm && _watching == null && _campaign.Profile != null)
            {
                // Campaign: pay out, unlock, save, then back to the circuits.
                var opponent = _campaign.Circuits.All[cm.circuit].Matches[cm.match].Character;
                int shopBefore = _campaign.Circuits.ShopYear(_campaign.Profile);
                var reward = _campaign.Circuits.Record(_campaign.Profile, cm.circuit, cm.match, _board.Winner > 1 ? 2 : _board.Winner,
                    loaner: _campaign.IsLoanerDeck(_pickDeck[0]));
                _campaign.EndLiveDuel(); // its record goes with this save
                // The Gauntlet cleared opens the Format Library (its scene after the champion's).
                bool libraryOpened = _campaign.Library.Open(_campaign.Profile, _campaign.Circuits);
                // A one-on-one circuit duel: its band's bounties count too.
                _campaign.AfterDuel(reward, _campaign.Circuits.All[cm.circuit].Band, _tally, _campaign.IsLoanerDeck(_pickDeck[0]), tag: false);
                _campaign.Save();
                BeginCampaignResults(cm.circuit, cm.match, reward, shopBefore, _board.Winner, libraryOpened);
                var headline = _board.Winner > 1 ? "Draw!" : _board.Winner == 0 ? "You win!" : $"{opponent.Name} wins!";
                var line = _board.Winner == 0 ? opponent.LoseLine : _board.Winner == 1 ? opponent.WinLine : "";
                if (_runner?.Result.Reason == DuelRunner.ReasonYataLock) line = YataLockLine(opponent.Name);
                _view.SetResult(headline, line, $"{reward.Summary}   ·   {_campaign.Profile.Dp:N0} DP");
                if (line.Length > 0) _view.AppendLog($"{opponent.Name}: \"{line}\"");
                _view.AppendLog($"Campaign: {reward.Summary}. Open Campaign to continue, or the Card Shop to spend DP.");
                return;
            }

            // Free and tag duels end with what's next on the banner (the campaign has its own results screen).
            if (_watching == null && _chainLesson == null)
                _view.SetResultButtons(new List<(string, Action)>
                {
                    ("Rematch", () => { seed++; _paused = false; NewDuel(null, _tag); UpdateControls(); }),
                    (_tag != null ? "Tag setup" : "New matchup", () => { if (_tag != null) _tagSetup.Open(); else ShowFreeDuel(); }),
                    ("Menu", () => _menu.Open()),
                });
            if (_board.Winner > 1)
            {
                _view.SetResult("Draw!", "");
                return;
            }
            if (_tag != null)
            {
                var lead = _players[_board.Winner];
                _view.SetResult($"{TeamName(_board.Winner)} win!", lead.WinLine);
                if (lead.WinLine.Length > 0) _view.AppendLog($"{lead.Name}: \"{lead.WinLine}\"");
                return;
            }
            var winner = _players[_board.Winner];
            var loser = _players[1 - _board.Winner];
            _view.SetResult(winner.Id == "you" ? "You win!" : $"{winner.Name} wins!",
                _runner?.Result.Reason == DuelRunner.ReasonYataLock ? $"{(loser.Id == "you" ? "You're" : $"{loser.Name} is")} Yata-locked: no cards and no draws left." : winner.WinLine);
            if (winner.WinLine.Length > 0) _view.AppendLog($"{winner.Name}: \"{winner.WinLine}\"");
            if (loser.LoseLine.Length > 0) _view.AppendLog($"{loser.Name}: \"{loser.LoseLine}\"");
        }

        /// <summary>
        /// A campaign result line when the duel ended by Yata-lock: who's locked (you, or <paramref name="opponent"/>). A tag duel
        /// (<paramref name="partner"/>: your partner's name) locks a team: "Kit &amp; Vera are Yata-locked!", "You and Hollis are ...".
        /// </summary>
        private string YataLockLine(string opponent, string partner = "") =>
            partner.Length == 0
                ? _board.Winner == 0 ? $"{opponent} is Yata-locked!" : "You're Yata-locked: no cards and no draws left."
                : _board.Winner == 0 ? $"{opponent} are Yata-locked!" : $"You and {partner} are Yata-locked: no cards and no draws left.";

        /// <summary>After a circuit duel: its scenes queued, then a moment to see the board and the results screen (DuelController.Results).</summary>
        private void BeginCampaignResults(int circuit, int match, DuelReward reward, int shopBefore, int winner, bool libraryOpened)
        {
            var events = Story.Events(_campaign.Circuits, circuit, reward);
            if (libraryOpened) events.Add("library");
            events.AddRange(Story.LossEvents(_campaign.Profile!, winner)); // the save's first loss: Gus's first-loss scene
            AddGrantEvents(events, reward);
            ShowResultsSoon(events, reward.Grants, story => ShowCampaignResults(circuit, match, reward, shopBefore, winner, libraryOpened, story));
        }

        private void ShowCampaignResults(int circuit, int match, DuelReward reward, int shopBefore, int winner, bool libraryOpened, List<CampaignState.QueuedStory> story)
        {
            var circuits = _campaign.Circuits;
            var c = circuits.All[circuit];
            var opponent = c.Matches[match].Character;
            var profile = _campaign.Profile!;
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? "Victory!" : "Defeat",
                Speaker = opponent.Name,
                Quote = winner == 0 ? opponent.LoseLine : winner == 1 ? opponent.WinLine : "",
                Circuit = $"{c.Name} · Match {match + 1} against {opponent.DisplayName}",
                TotalDp = profile.Dp,
                Progress = Enumerable.Range(0, c.Matches.Count).Select(m => profile.HasWon(circuit, m)).ToList(),
            };
            if (winner == 0)
            {
                int prize = reward.ClearedCircuit != null ? c.Prize : 0;
                if (_campaign.IsLoanerDeck(_pickDeck[0]))
                    // A loaner pays half the duel's DP (the prize in full): what was paid, not the circuit's rates.
                    r.Earnings.Add((reward.FirstWin ? "Win and first win (half: loaner deck)" : "Win (half: loaner deck)", reward.Dp - prize));
                else
                {
                    r.Earnings.Add(("Win", circuits.WinDp(circuit)));
                    if (reward.FirstWin) r.Earnings.Add(("First-win bonus", Circuits.FirstWinBonus));
                }
                if (reward.ClearedCircuit != null) r.Earnings.Add(($"{c.Name} prize", prize));
            }
            else r.Earnings.Add((winner == 1 ? "For the duel" : "Draw", reward.Dp));
            foreach (var up in reward.LevelUps.Where(u => u.Dp > 0)) r.Earnings.Add(($"Level {up.Level} reward", up.Dp));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (reward.Exp, profile.Level, into, needed);
            int wins = profile.WinsAgainst(opponent.Id), losses = profile.Losses.TryGetValue(opponent.Id, out var l) ? l : 0;
            r.Record = $"Against {opponent.Name}: <b>{wins}</b> win{(wins == 1 ? "" : "s")}, <b>{losses}</b> loss{(losses == 1 ? "" : "es")}\n"
                       + $"Circuits cleared: {circuits.ClearedCount(profile)} of {circuits.All.Count}";
            foreach (var up in reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}: trade {(up.Tokens > 1 ? "them" : "it")} for any card at the Card Trader" : ""));
            if (winner == 0 && RecipeBook.JustUnlocked(circuits, profile, circuit, match) is Recipe unlockedRecipe)
                r.Unlocks.Add($"Recipe unlocked: {unlockedRecipe.Owner.Name}'s {DeckTitle(unlockedRecipe.Deck)}. Find it in the Recipe Book.");
            if (reward.ClearedCircuit != null)
            {
                r.Unlocks.Add($"{c.Name} cleared!" + (reward.Tokens > 0 ? $" +{reward.Tokens} Card Token{(reward.Tokens > 1 ? "s" : "")}: trade for any card at the Card Trader" : ""));
                foreach (var cup in circuits.TagCups.Where(t => t.After == circuit))
                    r.Unlocks.Add($"New Tag Cup: {cup.Name}. Team up with a duelist you've beaten");
            }
            if (reward.UnlockedCircuit != null)
            {
                r.Unlocks.Add($"New circuit: {reward.UnlockedCircuit.Name} ({reward.UnlockedCircuit.Band})");
                int shopAfter = circuits.ShopYear(profile);
                if (shopAfter > shopBefore) r.Unlocks.Add($"Card Shop: sets up to {shopAfter} are on sale");
                // The first Expert circuit opens the OCG import shelf.
                if (circuits.IsLateBand(circuit + 1) && !circuits.IsLateBand(circuit) && ImportShelf.IsOpen(profile, circuits) && _campaign.Imports.Packs.Count > 0)
                    r.Unlocks.Add("Card Shop: the OCG import shelf is open. Packs of cards the TCG never printed");
                var next = circuit + 1;
                // (Only your team answers to the lists, and only in era-limited play.)
                if (profile.EraLimited && reward.UnlockedCircuit.Banlist.Length > 0 && reward.UnlockedCircuit.Banlist != c.Banlist)
                {
                    var problems = _campaign.Problems(_library.Load(profile.Deck), next).Count(p => p.Message.Contains(reward.UnlockedCircuit.Banlist));
                    r.Unlocks.Add($"New ban list ahead: {reward.UnlockedCircuit.Banlist}" + (problems > 0 ? $" ({problems} card{(problems == 1 ? "" : "s")} in your deck must go)" : ""));
                }
            }
            if (libraryOpened) r.Unlocks.Add($"The Format Library is open: {_campaign.Library.Cups.Count} historic cups, each under its year's list. Find it under Library on the circuit map");
            AddGrants(r, reward);
            _scenes.LastResults = r;
            _scenes.ShowResults(r,
                continueAction: () => PlayResultStory(story, () => _campaignScreen.Open(reward.Summary)),
                rematch: Requeued(story, () => ShowVersus(circuit, match)),
                shop: Requeued(story, () => _shop.Open()));
        }

        private void OnDestroy() => _duel?.Dispose();

        // Replay key moments (found on another thread) and the Replay Lesson being watched.
        private Replay? _momentsFor;
        private List<Ygo.Engine.Replays.Moment>? _moments;
        private readonly object _pendingLock = new object();
        private (Replay replay, List<Ygo.Engine.Replays.Moment> moments)? _pendingMomentsValue;
        private (Replay replay, List<Ygo.Engine.Replays.Moment> moments)? _pendingMoments
        {
            get { lock (_pendingLock) return _pendingMomentsValue; }
            set { lock (_pendingLock) _pendingMomentsValue = value; }
        }
        private int _lessonTurn;
        private Replay? _lessonReplay;
        private string _lessonTitle = "", _lessonSummary = "";
        private bool _lessonBusy;
        private (Recipe recipe, ReplayLesson.Result lesson, List<Ygo.Engine.Replays.Moment> moments)? _pendingLessonValue;
        private string? _lessonError;

        private string LessonFolder => Path.Combine(_replays.SavedFolder, "lessons");

        /// <summary>
        /// A Replay Lesson for a recipe: loads it (lessons are kept in mods/replays/lessons) or makes one on another
        /// thread: the recipe's owner plays their deck against another duelist of that circuit, and the duel opens at
        /// the owner's key turn.
        /// </summary>
        private void PrepareLesson(Recipe recipe)
        {
            if (_lessonBusy) return;
            Directory.CreateDirectory(LessonFolder);
            var file = Path.Combine(LessonFolder, $"{recipe.Owner.Id}-{recipe.Deck}.txt");
            var cards = _cardPack!;
            var scripts = _scripts;
            var circuit = _campaign.Circuits.All[recipe.Circuit];
            var rivalMatch = circuit.Matches.FirstOrDefault(m => m.Character.Id != recipe.Owner.Id && _library.Contains(m.Deck));
            var rival = rivalMatch?.Character ?? _roster.First(p => p.Id == "old-gus");
            var rivalDeckName = rivalMatch?.Deck ?? "zombies";
            if (!_library.Contains(recipe.Deck)) return;
            var deck = _library.Load(recipe.Deck);
            var rivalDeck = _library.Load(rivalDeckName);
            _lessonBusy = true;
            _recipeBook.SetStatus(File.Exists(file) ? "Opening the lesson..." : $"Preparing the lesson: {recipe.Owner.Name} is playing it through (about 10 seconds; only the first time).");
            System.Threading.Tasks.Task.Run(() =>
            {
                ReplayLesson.Result lesson;
                var saved = File.Exists(file) ? File.ReadAllLines(file) : null;
                if (saved != null && saved.Length >= 3 && int.TryParse(saved[0], out var turn) && Replay.FromCode(saved[2]) is Replay stored)
                    lesson = new ReplayLesson.Result { Replay = stored, Turn = turn, Summary = saved[1] };
                else
                {
                    lesson = ReplayLesson.Make(recipe.Owner, deck, recipe.Deck, rival, rivalDeck, rivalDeckName, cards, scripts);
                    File.WriteAllLines(file, new[] { lesson.Turn.ToString(), lesson.Summary, lesson.Replay.ToCode() });
                }
                return (lesson, Ygo.Engine.Replays.ReplayMoments.Find(lesson.Replay, cards, scripts));
            }).ContinueWith(t =>
            {
                lock (_pendingLock)
                {
                    if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) _pendingLessonValue = (recipe, t.Result.lesson, t.Result.Item2);
                    else _lessonError = t.Exception?.GetBaseException().Message ?? "it didn't start";
                }
            });
        }

        /// <summary>Starts a prepared lesson (called every frame).</summary>
        private void TakeLesson()
        {
            (Recipe recipe, ReplayLesson.Result lesson, List<Ygo.Engine.Replays.Moment> moments)? ready;
            string? error;
            lock (_pendingLock) { ready = _pendingLessonValue; error = _lessonError; _pendingLessonValue = null; _lessonError = null; }
            if (error != null) { _lessonBusy = false; _recipeBook.SetStatus($"<color=#FF8A8A>Couldn't make the lesson:</color> {error}"); }
            if (ready is not { } r) return;
            _lessonBusy = false;
            // The lesson plays on the duel screen, with nothing over it: the book (and the shop, when the book came from
            // there) step aside without their Closed. The book comes back when the lesson ends (EndLesson), and closing it
            // goes back to the shop or the campaign it was opened from.
            _recipeBook.Hide();
            _shop.Hide();
            _campaignScreen.Close();
            _lessonReplay = r.lesson.Replay;
            _lessonTurn = r.lesson.Turn;
            _lessonTitle = $"{r.recipe.Owner.Name}'s {DeckTitle(r.recipe.Deck)}";
            _lessonSummary = $"Key {r.lesson.Summary}. Opened just before it; press Pause or Step to watch, Moments for the rest.";
            _momentsFor = r.lesson.Replay;
            _moments = r.moments;
            var start = r.moments.FirstOrDefault(m => m.Kind == Ygo.Engine.Replays.MomentKind.Turn && m.Turn == r.lesson.Turn)?.Tick ?? 0;
            speedIndex = Mathf.Min(speedIndex, 3); // a lesson is for watching: 4 events/s at most
            WatchReplay(r.lesson.Replay, start);
            _view.SetMomentsReady(true);
        }

        /// <summary>Leaves a lesson for the Recipe Book.</summary>
        private void EndLesson()
        {
            _lessonReplay = null;
            _lessonTurn = 0;
            _view.SetLesson(null, null, null);
            _paused = true;
            UpdateControls();
            _recipeBook.Open();
        }

        /// <summary>Picks up moments found on the other thread (called every frame).</summary>
        private void TakeMoments()
        {
            if (_pendingMoments is not { } found) return;
            _pendingMoments = null;
            if (found.replay != _momentsFor) return;
            _moments = found.moments;
            _view.SetMomentsReady(true);
        }

        private DuelNarrator? _narrator;
        private DuelHistory? _history;

        private bool _settingsFromDuel, _pausedBeforeSettings;

        /// <summary>
        /// The controller's Back: the deck editor's own back, else closes the card reader or a card's action menu (the cursor goes back to
        /// the card), else declines or finishes the prompt.
        /// </summary>
        private void ControllerBack()
        {
            if (_about.IsOpen) _about.Back();
            else if (_import.IsOpen) _import.Back();
            else if (_editor.IsOpen) _editor.ControllerBack();
            else if (_bracketScreen.IsOpen || _bracketScreen.EntryOpen) _bracketScreen.ControllerBack();
            else if (_view.ReaderOpen) _view.CloseReader();
            else if (_view.ActionMenuOpen) _view.CloseActionMenu(returnFocus: true);
            else _view.Prompt.Back();
        }

        /// <summary>When the game stops to ask "Chain?": Auto (the default), On or Off. A saved setting.</summary>
        public static ResponseMode ResponseSetting
        {
            get => (ResponseMode)Mathf.Clamp(PlayerPrefs.GetInt("responses", (int)ResponseMode.Auto), 0, 2);
            set { PlayerPrefs.SetInt("responses", (int)value); PlayerPrefs.Save(); }
        }

        /// <summary>Held down (Shift, or a controller's left trigger): ask at the next chance to respond whatever the mode.</summary>
        public static bool HoldingAsk()
        {
#if ENABLE_INPUT_SYSTEM
            return UnityEngine.InputSystem.Keyboard.current?.shiftKey.isPressed == true
                   || ControllerSupport.IsHeld(PadAction.Ask);
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        private void CycleResponses()
        {
            if (_chainLesson != null)
            {
                _lessonMode = _lessonMode switch { ResponseMode.Auto => ResponseMode.On, ResponseMode.On => ResponseMode.Off, _ => ResponseMode.Auto };
                if (_human != null) _human.Presenter.Mode = _lessonMode;
                _view.SetResponseMode(_human != null, _lessonMode);
                return;
            }
            ResponseSetting = ResponseSetting switch { ResponseMode.Auto => ResponseMode.On, ResponseMode.On => ResponseMode.Off, _ => ResponseMode.Auto };
            if (_human != null) _human.Presenter.Mode = ResponseSetting;
            _view.SetResponseMode(_human != null, ResponseSetting);
        }

        /// <summary>A deck's display name: its #title note, else its file name in words ("burning-abyss" -> "Burning Abyss").</summary>
        private string DeckTitle(string deck) => _campaign.DeckTitle(deck);

        private readonly Reactions _reactions = new Reactions();
        private readonly System.Random _reactionRng = new System.Random();

        /// <summary>Duelists react to what just happened: an expression, a flash, and a line in a speech bubble.</summary>
        private void React()
        {
            if (_view.Fx.Tempo <= 0) return; // no reactions while fast-forwarding
            var lp = new[] { _board.Players[0].LifePoints, _board.Players[1].LifePoints };
            foreach (var (player, kind, delay) in _reactions.Read(_events, _cardPack!, lp, Time.unscaledTime))
            {
                var id = _view.PortraitId(player);
                var who = _roster.FirstOrDefault(r => r.Id == id);
                if (who == null) continue; // you, or someone without a profile
                _view.React(player, kind, Reactions.Line(who, kind, _reactionRng), delay * _view.Fx.Tempo);
            }
        }

        private void UpdateControls()
        {
            _view.SetControls(_paused, Speeds[PlaySpeed]);
            // Animations play at full length up to 4 events/s, get quicker above that, and switch off at 60/s (a combo route
            // playing runs at Normal speed with "Slow down for combos" on, DuelController.Show).
            var speed = Speeds[PlaySpeed];
            var tempo = speed <= 4 ? 1f : speed >= 60 ? 0f : 4f / (speed + 2.5f);
            _view.Fx.Tempo = tempo;
            _view.LifePointRollTime = 0.7f * tempo;
        }

        private static bool IsVisible(byte message) => Ygo.Engine.Replays.Visibility.IsVisible(message);


        /// <summary>
        /// Builds read data from StreamingAssets; in the editor it falls back to the repo checkout
        /// (one level above the Unity project) so `git pull` on vendor/ updates cards instantly.
        /// </summary>
        private static string FindDataFolder(string streamingName, string repoRelative)
        {
            var streaming = Path.Combine(Application.streamingAssetsPath, streamingName);
            if (Directory.Exists(streaming)) return streaming;
            var repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", repoRelative));
            if (Directory.Exists(repo)) return repo;
            throw new DirectoryNotFoundException($"Could not find {streamingName} in StreamingAssets or at {repo}");
        }
    }
}
