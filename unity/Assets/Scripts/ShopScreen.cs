using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The card shop, Gus's Cards (SHOP-REWORK-SPEC-2026-11-01): one list in sections (<see cref="ShopShelves"/>): the
    /// featured shelf for your current circuit (the sets its opponents play from, and soon rows that open with levels),
    /// New arrivals, Gus's house packs of the decks you're about to face, the OCG imports (from the Expert band on, not
    /// in era-limited play) and the back shelves (every other set on sale, collapsed). A set's detail says why it's
    /// there (your wishlist, who plays its cards, your decks), its set progress and its odds; featured sets sell boxes
    /// of 24. Opening a pack lays out its cards face down; click them (or Reveal all) to flip them. Hits glow before they
    /// turn over and burst when they do, and every kept card says why it matters. Gus and Kit speak in the status bar
    /// and under a big pull (<see cref="ShopVoice"/>).
    /// </summary>
    public sealed class ShopScreen : MonoBehaviour
    {
        private const int PerPage = 11;

        private CampaignState _state = null!;
        private ICardDataProvider _cards = null!;
        private RectTransform _root = null!, _list = null!, _detail = null!, _opening = null!, _packCards = null!;
        private Text _plate = null!, _dp = null!, _meta = null!, _pageLabel = null!, _status = null!, _openingTitle = null!, _openingSummary = null!;
        /// <summary>Under the set's name: a set this pack completed (Gus frames a card and a poster), a Prize Pack's slab odds; empty otherwise.</summary>
        private Text _openingUnlocks = null!;
        /// <summary>Under the summary: Gus or Kit on the pack's best pull, the box, a Prize Pack's slab.</summary>
        private Text _openingVoice = null!;
        /// <summary>Copies the last pack's set completion paid (beside the pack's own cards).</summary>
        private int _unlockCopies;
        private Button _openAnother = null!;
        private GameObject _revealAll = null!;
        private System.Random _rng = new System.Random();
        private readonly List<PackCard> _pack = new List<PackCard>();

        // This visit's list: the shelves, the rows shown (headers included), the pick, the back shelves open or not.
        private ShopShelves? _shelves;
        private TagContext? _tags;
        private List<ShelfRow> _rows = new List<ShelfRow>();
        private readonly Dictionary<PackSet, ShelfRow> _rowOf = new Dictionary<PackSet, ShelfRow>();
        private PackSet? _selected;
        /// <summary>The back shelves' row is selected (its line in the detail panel).</summary>
        private bool _backSelected;
        private bool _backOpen;
        private int _page;
        private string _filter = "";
        // Wishlist order: sets ranked by how many of your wishlist cards a pack gives (cached per opening of the shop).
        private bool _byWishlist;
        private Text _wishLabel = null!;
        private InputField _search = null!;
        private Dictionary<PackSet, double> _wishValue = new Dictionary<PackSet, double>();
        // A row's second line and a set's "Why this set", worked out once a visit.
        private readonly Dictionary<PackSet, string> _rowLine = new Dictionary<PackSet, string>();
        private readonly Dictionary<PackSet, List<string>> _why = new Dictionary<PackSet, List<string>>();
        // Gus's lines this visit: the status bar's, the featured shelf's, the odds block's, the back shelves'.
        private string _footer = "";
        private VoiceLine? _shelfLine, _oddsLine, _backLine;
        private bool _oddsPicked, _backPicked;
        /// <summary>This visit's previous pack had a pull line (the spacing rule).</summary>
        private bool _lastPackHadLine;

        public event Action? Closed;
        /// <summary>The Card Trader button (spend Card Tokens on any card), the Structure Decks and the Recipe Book.</summary>
        public event Action? TraderClicked, StructureClicked, RecipeClicked;
        private Text _traderLabel = null!;

        /// <summary>
        /// After the Structure Decks, the Card Trader or the Recipe Book (a recipe pack bought there): the stock worked out
        /// again (set progress, tags, house packs), then the list and the detail shown, with the DP and token counts.
        /// </summary>
        public void RefreshHeader()
        {
            if (!IsOpen || _state.Profile == null) return;
            Restock();
            _rows = RowsNow();
            if (_backSelected) ShowBack();
            else if (_selected != null) Select(_selected);
            else ShowList();
        }
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        /// <summary>What the opening screen shows: a bought pack, a box's best pulls, or a bracket's Prize Packs.</summary>
        private enum Showing { Pack, Box, Prize }
        private Showing _showing;

        private sealed class PackCard
        {
            public Pull Pull;
            public bool New, Revealed;
            /// <summary>Glows, bursts and plays the rare sounds (<see cref="PackOdds.IsHit"/>).</summary>
            public bool Hit;
            /// <summary>A box's promised card (shown last).</summary>
            public bool Topper;
            /// <summary>A Prize Pack's sealed slab: its grade (0 for every other card).</summary>
            public int Slab;
            /// <summary>Kept, kept in place of a less rare copy, or (a 4th copy) turned into DP.</summary>
            public Added Added;
            /// <summary>Why the card matters: your wishlist, who plays it, your decks.</summary>
            public PackCardTags? Tags;
            public RectTransform Slot = null!, Back = null!, Face = null!;
            public RawImage? Glow;
            /// <summary>Shows the face-up card in the side panel (set as it turns over).</summary>
            public Action? Read;
        }

        public void Init(CampaignState state, ICardDataProvider cards)
        {
            _state = state;
            _cards = cards;
            Build();
            _root.gameObject.SetActive(false);
        }

        /// <summary>Gauge &amp; Gilt's Grading Desk (grading, the Sell desk, the used market).</summary>
        public event Action? GradingClicked;
        private Text _deskLabel = null!;

        /// <summary>
        /// Opens the shop. <paramref name="visit"/>: a visit from the campaign (not coming back from the Grading Desk or a
        /// check): it counts for New arrivals (<see cref="Arrivals.Visit"/>), Gus's footer line plays, and the default pick
        /// is selected. Either way the stock is worked out again.
        /// </summary>
        public void Open(bool visit = true)
        {
            var T = UiTheme.Current;
            _byWishlist = false;
            var profile = _state.Profile;
            int ready = profile == null ? 0 : GradingDesk.Ready(profile);
            _deskLabel.text = ready > 0 ? $"Grading Desk <color={T.GoodHex}>({ready})</color>" : "Grading Desk";
            _root.gameObject.SetActive(true);
            _opening.gameObject.SetActive(false);
            if (_state.Shop == null || _state.Shop.Sets.Count == 0 || profile == null)
            {
                _status.text = "The shop needs the card-set data (StreamingAssets/printings.bin). See the README to generate it.";
                return;
            }
            var arrived = new List<int>();
            if (visit)
            {
                arrived = Arrivals.Visit(profile, _state.ShopYear);
                _shelfLine = null;
                _oddsPicked = _backPicked = false;
                _lastPackHadLine = false;
                _backOpen = false;
                _selected = null;
            }
            Restock();
            _plate.text = _state.Voice.Pick("header", profile, _state.Circuits, NoFill)?.Text ?? "CARD SHOP";
            if (visit)
            {
                // Gus's line for the visit (an arrival takes it), and the featured shelf's (call after Visit: see ShopVoice.Footer).
                _footer = Say(_state.Voice.Footer(profile, _state.Circuits, arrived));
                var circuit = _shelves!.Shelf == FeaturedShelves.PostGame ? "" : _shelves.Shelf;
                _shelfLine = _state.Voice.Pick("shelf:" + _shelves.Shelf, profile, _state.Circuits, new Dictionary<string, string> { ["circuit"] = circuit });
                _state.Save();
                _status.text = _footer.Length > 0 ? _footer : StandingHelp;
            }
            Filter(_filter);
            if (_shelves!.OnSale.Count == 0) { _status.text = "No sets are open yet."; return; }
            var pick = _selected != null && (_shelves.OnSale.Contains(_selected) || _rowOf.ContainsKey(_selected)) ? _selected : _shelves.DefaultPick ?? _shelves.OnSale[0];
            Select(pick, page: true);
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>
        /// Steps aside without <see cref="Closed"/>: the editor or a lesson opened from the Recipe Book over the shop (the
        /// shop's canvas is above the editor's). <see cref="Open"/> with visit: false brings it back.
        /// </summary>
        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>The standing help: shown when no line of Gus's has the status bar, and in the detail panel.</summary>
        private const string StandingHelp = "Pick a set on the left. Packs go straight into your collection; build with them in Campaign > Edit deck.";

        private static readonly IReadOnlyDictionary<string, string> NoFill = new Dictionary<string, string>();

        /// <summary>A shop line as it shows: the speaker's short name, then the line in quotes; a label ("-") as plain text.</summary>
        private string Say(VoiceLine? line)
        {
            if (line == null) return "";
            var T = UiTheme.Current;
            if (line.IsLabel) return line.Text;
            return $"<color={T.GoldSoftHex}>{_state.SpeakerName(line.Speaker)}</color>  {T.Paint($"\"{line.Text}\"", T.Quote)}";
        }

        // ---- Shop ------------------------------------------------------------------------------------

        private void Build()
        {
            var T = UiTheme.Current;
            _root = Ui.Overlay(transform, "Shop", 14);
            Ui.Image(_root, T.Base);
            Ui.Image(Ui.Box(_root, 0, 0, 1920, 90), T.Deep);
            _plate = Ui.Text(Ui.Box(_root, 40, 0, 300, 90), "CARD SHOP", 34, T.Sign, TextAnchor.MiddleLeft);
            _plate.fontStyle = FontStyle.Bold;
            _dp = Ui.Text(Ui.Box(_root, 340, 10, 490, 40), "", 26, T.Gold, TextAnchor.MiddleLeft);
            _meta = Ui.Text(Ui.Box(_root, 340, 50, 490, 30), "", 16, T.TextDim, TextAnchor.MiddleLeft);
            _meta.resizeTextForBestFit = true;
            _meta.resizeTextMinSize = 12;
            _meta.resizeTextMaxSize = 16;
            Ui.BoxButton(_root, 840, 22, 180, 46, "Recipe Book", () => RecipeClicked?.Invoke(), 17);
            var desk = Ui.BoxButton(_root, 1032, 22, 200, 46, "Grading Desk", () => GradingClicked?.Invoke(), 17);
            _deskLabel = desk.GetComponentInChildren<Text>();
            _deskLabel.supportRichText = true;
            Ui.BoxButton(_root, 1244, 22, 210, 46, "Structure Decks", () => StructureClicked?.Invoke(), 17);
            var trader = Ui.BoxButton(_root, 1466, 22, 260, 46, "", () => TraderClicked?.Invoke(), 17);
            _traderLabel = trader.GetComponentInChildren<Text>();
            Ui.BoxButton(_root, 1740, 22, 140, 46, "Back", Close, 18);

            _search = Ui.Input(Ui.Box(_root, 40, 110, 630, 44), "Search sets: name or code (e.g. \"legend\", \"RA01\", \"2005\")", 16, v => Filter(v));
            var wish = Ui.BoxButton(_root, 680, 110, 180, 44, "", ToggleWishlist, 16);
            _wishLabel = wish.GetComponentInChildren<Text>();
            _list = Ui.Box(_root, 40, 166, 820, PerPage * 70);
            Ui.BoxButton(_root, 40, 950, 140, 40, "< Prev", () => { _page--; ShowList(); });
            _pageLabel = Ui.Text(Ui.Box(_root, 190, 950, 520, 40), "", 16, T.Text);
            Ui.BoxButton(_root, 720, 950, 140, 40, "Next >", () => { _page++; ShowList(); });

            _detail = Ui.Box(_root, 900, 110, 980, 880);
            Ui.Image(_detail, T.Panel);
            _status = Ui.Text(Ui.Box(_root, 40, 1010, 1840, 50), "", 16, T.TextSoft, TextAnchor.MiddleLeft);
            _status.supportRichText = true;

            BuildOpening();
        }

        /// <summary>Works the visit's list out again: the shelves, the tags, and the rows' lines (after anything that changes them).</summary>
        private void Restock()
        {
            _shelves = _state.Shelves();
            _tags = _state.Tags();
            _rowOf.Clear();
            _rowLine.Clear();
            _why.Clear();
            if (_shelves == null) return;
            foreach (var r in _shelves.Featured.Concat(_shelves.NewArrivals).Concat(_shelves.House).Concat(_shelves.Imports).Concat(_shelves.Back))
                if (r.Set != null && !_rowOf.ContainsKey(r.Set)) _rowOf[r.Set] = r;
        }

        /// <summary>
        /// Sets you can buy, each once, in the list's order: the featured shelf's live sets (released by the shop's year:
        /// your band's era plus a year a level), New arrivals, Gus's house packs (the current and the next circuit's decks
        /// you haven't beaten), the OCG imports, and the back shelves.
        /// </summary>
        private IReadOnlyList<PackSet> Available() => _shelves?.OnSale ?? (IReadOnlyList<PackSet>)Array.Empty<PackSet>();

        /// <summary>For automated checks: the names of the sets on sale now (the stock worked out again), in the shop's order.</summary>
        public List<string> DebugAvailable() => (_state.Shelves()?.OnSale ?? Available()).Select(set => set.Info.Name).ToList();

        /// <summary>For automated checks: shows a set's details by name (true if it's on sale, or a soon row).</summary>
        public bool DebugSelect(string name)
        {
            var set = Available().Concat(_rowOf.Keys).FirstOrDefault(x => x.Info.Name == name);
            if (set == null) return false;
            Select(set, page: true);
            return true;
        }

        /// <summary>For automated checks: opens the shop in wishlist order; returns how many sets have wishlist cards.</summary>
        public int DebugWishlist()
        {
            Open(visit: false);
            ToggleWishlist();
            return _byWishlist ? _rows.Count : 0;
        }

        private void ToggleWishlist()
        {
            var wanted = _state.Profile?.Wanted() ?? new HashSet<uint>();
            if (!_byWishlist && wanted.Count == 0)
            {
                _status.text = "Your wishlist is empty. In the campaign's deck editor, Recipe puts the cards a deck is missing on it.";
                return;
            }
            _byWishlist = !_byWishlist;
            if (_byWishlist)
            {
                _wishValue = Available().ToDictionary(s => s, s => PackCatalog.Expected(s, wanted, 120));
                _status.text = $"Sets ranked by your wishlist for {_state.DeckTitle(_state.Profile!.WishlistFor)} ({wanted.Count} cards you still need).";
            }
            Filter(_filter);
        }

        /// <summary>The rows shown: the shelves in sections (a search opens the back shelves), or in wishlist order one flat ranked list.</summary>
        private List<ShelfRow> RowsNow()
        {
            if (_shelves == null) return new List<ShelfRow>();
            if (!_byWishlist) return _shelves.Rows(_backOpen, _filter);
            return Available().Where(s => _wishValue.TryGetValue(s, out var v) && v > 0).OrderByDescending(s => _wishValue[s])
                .Where(s => _filter.Length == 0 || s.Info.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0
                            || s.Info.Code.Equals(_filter, StringComparison.OrdinalIgnoreCase) || s.Year.StartsWith(_filter))
                .Select(s => _rowOf.TryGetValue(s, out var r) ? r : new ShelfRow { Set = s }).ToList();
        }

        private void Filter(string text)
        {
            _filter = text.Trim();
            _wishLabel.text = _byWishlist ? "All sets" : "Wishlist order";
            _rows = RowsNow();
            _page = 0;
            ShowList();
        }

        /// <summary>The back shelves opened or closed (its row stays selected, with Gus's line in the detail panel).</summary>
        private void ToggleBack(bool? open = null)
        {
            _backOpen = open ?? !_backOpen;
            _rows = RowsNow();
            ShowBack();
        }

        private void ShowList()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            _traderLabel.text = $"Card Trader · {profile.Tokens} token{(profile.Tokens == 1 ? "" : "s")}";
            // Sets are open up to your band's era plus a year per level since, up to the next band's era; the rest
            // wait for later circuits. The header and the page line below say the same rule with the same numbers.
            int year = _state.Circuits.ShopYear(profile), next = _state.Circuits.NextEra(profile);
            int topLevel = profile.Level + next - year; // the level that opens the next band's era
            var era = year < next ? $"Sets up to {year}, to {next} at level {topLevel}" : $"Sets up to {year}";
            _dp.text = $"{profile.Dp:N0} DP";
            _meta.text = $"{era} · {profile.Collection.Total} cards · {profile.PacksOpened} pack{(profile.PacksOpened == 1 ? "" : "s")}";
            for (int i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            int pages = Math.Max(1, (_rows.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var all = _state.Shop?.Sets ?? (IReadOnlyList<PackSet>)Array.Empty<PackSet>();
            int onSale = all.Count(s => int.TryParse(s.Year, out var y) && y <= year);
            var locked = all.Count - onSale;
            // Of the locked sets, the ones up to the next band's era open with levels; the rest with later circuits.
            int byLevel = year >= next ? 0 : all.Count(s => int.TryParse(s.Year, out var y) && y > year && y <= next);
            int byCircuit = locked - byLevel;
            var waiting = new List<string>();
            if (byLevel > 0) waiting.Add($"{byLevel} more by level {topLevel}");
            if (byCircuit > 0) waiting.Add($"{byCircuit} with later circuits");
            int shown = _byWishlist || _filter.Length > 0 ? _rows.Count(r => r.Kind == ShelfRowKind.Set && !r.Locked) : Available().Count;
            _pageLabel.text = $"{_page + 1} / {pages}   ({shown} sets" + (waiting.Count > 0 ? "; " + string.Join(", ", waiting) + ")" : ")");
            for (int row = 0; row < PerPage; row++)
            {
                int i = _page * PerPage + row;
                if (i >= _rows.Count) break;
                DrawRow(_rows[i], row);
            }
        }

        private void DrawRow(ShelfRow r, int slot)
        {
            var T = UiTheme.Current;
            var rt = Ui.Box(_list, 0, slot * 70, 820, 64);
            switch (r.Kind)
            {
                case ShelfRowKind.Header:
                {
                    // "GUS'S HOUSE PACKS · the decks you'll face": the name in gold, the rest quieter.
                    var parts = r.Header.Split(new[] { " · " }, 2, StringSplitOptions.None);
                    var text = parts[0] + (parts.Length > 1 ? $"  <color={T.TextDimHex}>{parts[1]}</color>" : "");
                    bool line = r.Section == ShelfSection.Featured && _shelfLine != null && !_byWishlist;
                    var head = Ui.Text(Ui.Box(rt, 14, line ? 2 : 0, 790, line ? 26 : 64), text, 17, T.Gold, line ? TextAnchor.MiddleLeft : TextAnchor.LowerLeft);
                    head.fontStyle = FontStyle.Bold;
                    if (line)
                    {
                        // The shelf's line, under its header: why Gus put these sets up front.
                        var said = Ui.Text(Ui.Box(rt, 14, 29, 790, 35), Say(_shelfLine), 13, T.TextSoft, TextAnchor.UpperLeft);
                        said.resizeTextForBestFit = true;
                        said.resizeTextMinSize = 11;
                        said.resizeTextMaxSize = 13;
                    }
                    Ui.Image(Ui.Box(rt, 0, 66, 820, 1), T.Divider);
                    break;
                }
                case ShelfRowKind.YearHeader:
                    Ui.Text(Ui.Box(rt, 14, 0, 790, 62), r.Header, 16, T.Caption, TextAnchor.LowerLeft).fontStyle = FontStyle.Bold;
                    break;
                case ShelfRowKind.BackToggle:
                {
                    Ui.Image(rt, _backSelected ? T.RowSelected : slot % 2 == 0 ? T.RowA : T.RowB);
                    Ui.Text(Ui.Box(rt, 14, 6, 790, 28), r.Header, 18, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                    // A search keeps them open (it looks through every set), so the row says so instead of offering to close.
                    Ui.Text(Ui.Box(rt, 14, 34, 790, 26), _filter.Length > 0 ? "Every other set on sale, newest year first · open while you search"
                        : _backOpen ? "Every other set on sale, newest year first · click to close"
                        : "Every other set on sale, by year · click to open", 15, T.TextFaint, TextAnchor.MiddleLeft);
                    Ui.Clickable(rt, () => ToggleBack(), hover: () => _keys.Point(_page * PerPage + slot), style: HoverStyle.Row);
                    break;
                }
                case ShelfRowKind.More:
                    Ui.Text(Ui.Box(rt, 14, 0, 790, 64), r.Header, 15, T.TextFaint, TextAnchor.MiddleLeft).fontStyle = FontStyle.Italic;
                    Ui.Clickable(rt, () => ToggleBack(true), style: HoverStyle.Row);
                    break;
                default:
                    DrawSet(rt, r, slot);
                    break;
            }
        }

        private void DrawSet(RectTransform rt, ShelfRow r, int slot)
        {
            var T = UiTheme.Current;
            var set = r.Set!;
            bool real = IsReal(set);
            Ui.Image(rt, set == _selected && !_backSelected ? T.RowSelected : slot % 2 == 0 ? T.RowA : T.RowB);
            // Chips after the name: NEW (the pack-reveal tag's color) and RARITY SET.
            var chips = new List<(string text, Color fill, Color ink, float width)>();
            if (r.New) chips.Add(("NEW", Ui.Hex("#E8433F"), T.Text, 50));
            if (real && PackCatalog.IsRaritySet(set)) chips.Add(("RARITY SET", T.GoldWash, T.GoldSoft, 104));
            // A soon row is greyed out, its chips too.
            if (r.Locked) chips = chips.Select(c => (c.text, T.ButtonOff, T.TextDim, c.width)).ToList();
            float room = 640 - chips.Sum(c => c.width + 8);
            var name = Ui.Text(Ui.Box(rt, 14, 6, room, 28), set.Info.Name, 17, r.Locked ? T.TextDim : T.Text, TextAnchor.MiddleLeft);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 12;
            name.resizeTextMaxSize = 17;
            float x = 14 + Mathf.Min(name.preferredWidth, room) + 10;
            foreach (var chip in chips)
            {
                var box = Ui.Box(rt, x, 9, chip.width, 22);
                Ui.Image(box, chip.fill).raycastTarget = false;
                Ui.Text(box, chip.text, 12, chip.ink).fontStyle = FontStyle.Bold;
                x += chip.width + 8;
            }
            var wishText = _byWishlist && _wishValue.TryGetValue(set, out var perPack) ? $" · <color={T.GoldSoftHex}>≈ {perPack:0.0} wishlist cards per pack</color>" : "";
            var second = Ui.Text(Ui.Box(rt, 14, 34, 640, 26), RowLine(r) + wishText, 15, T.TextFaint, TextAnchor.MiddleLeft);
            second.supportRichText = true;
            Ui.Text(Ui.Box(rt, 660, 0, 150, 64), $"{set.Price:N0} DP", 17, r.Locked ? T.TextDim : T.Gold, TextAnchor.MiddleRight);
            Ui.Clickable(rt, () => Select(set), hover: () => _keys.Point(_page * PerPage + slot), style: HoverStyle.Row);
        }

        /// <summary>A real booster set (not a house pack or an OCG import): it has a year, set progress, a box when featured.</summary>
        private static bool IsReal(PackSet set) => !SelectionPacks.IsHousePack(set) && !ImportShelf.IsImport(set);

        /// <summary>
        /// A row's second line: "DB1 · 2004 · 41/250 · 3 wanted · Tink plays 2" (each part only above 0), "Arrives at level
        /// 14" on a soon row, "House Pack · 41 cards · you have 12", "OCG import · 30 cards · you have 2".
        /// </summary>
        private string RowLine(ShelfRow r)
        {
            var set = r.Set!;
            if (_rowLine.TryGetValue(set, out var cached)) return cached;
            var T = UiTheme.Current;
            var parts = new List<string>();
            if (SelectionPacks.IsHousePack(set)) parts.AddRange(new[] { "House Pack", $"{set.CardCount} cards", $"you have {_state.OwnedFromSet(set)}" });
            else if (ImportShelf.IsImport(set)) parts.AddRange(new[] { $"<color={T.ImportHex}>OCG import</color>", $"{set.CardCount} cards", $"you have {_state.OwnedFromSet(set)}" });
            else
            {
                parts.Add($"{set.Info.Code} · {set.Year}");
                if (r.Locked) parts.Add($"<color={T.WarnHex}>Arrives at level {r.OpensAtLevel}</color>");
                var (owned, total) = _state.SetProgress(set);
                if (owned > 0) parts.Add(owned >= total ? $"<color={T.GoodHex}>{owned}/{total} ✓</color>" : $"{owned}/{total}");
            }
            if (_tags != null)
            {
                var (wanted, played, first) = PackTags.Count(set, _tags);
                if (wanted > 0) parts.Add($"<color={T.GoldSoftHex}>{wanted} wanted</color>");
                if (played > 0 && first.Length > 0 && !SelectionPacks.IsHousePack(set)) parts.Add($"<color={T.InfoHex}>{first} plays {played}</color>");
            }
            return _rowLine[set] = string.Join(" · ", parts);
        }

        /// <summary>Selects a set: its row lit, its detail shown; <paramref name="page"/>: turn to its page.</summary>
        private void Select(PackSet set, bool page = false)
        {
            _selected = set;
            _backSelected = false;
            if (page)
            {
                int at = _rows.FindIndex(r => r.Set == set);
                if (at >= 0) _page = at / PerPage;
            }
            ShowList();
            ShowSet(set, _rowOf.TryGetValue(set, out var row) ? row : new ShelfRow { Set = set });
        }

        /// <summary>The back shelves' row selected: Gus's line about them in the detail panel.</summary>
        private void ShowBack()
        {
            var T = UiTheme.Current;
            _backSelected = true;
            ShowList();
            ClearDetail();
            Ui.Text(Ui.Box(_detail, 30, 24, 920, 44), "Back shelves", 28, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            int n = _shelves?.Back.Count ?? 0;
            var years = _shelves?.Back.Select(r => r.Set!.Year).Where(y => y.Length > 0).ToList() ?? new List<string>();
            Ui.Text(Ui.Box(_detail, 30, 70, 920, 28), years.Count > 0 ? $"{n} sets from {years.Min()} to {years.Max()}, newest year first · sets with commons first in each year" : $"{n} sets",
                16, T.TextMuted, TextAnchor.MiddleLeft);
            if (!_backPicked && _state.Profile != null)
            {
                _backPicked = true;
                _backLine = _state.Voice.Pick("back-shelves", _state.Profile, _state.Circuits, NoFill);
                _state.Save();
            }
            float y = 120;
            if (_backLine != null) y = Line(y, Say(_backLine), 17, T.TextSoft) + 12;
            y = Line(y, "Everything on sale that isn't on the featured shelf or in New arrivals: nothing leaves the shop, it moves to the back. "
                        + "Search finds any set by name, code or year.", 16, T.TextFaint) + 12;
            Line(y, (_filter.Length > 0 ? "They stay open while you search." : _backOpen ? "Click the row again to close the back shelves." : "Click the row again to open them.")
                    + " " + StandingHelp, 16, T.TextFaint);
        }

        private void ClearDetail()
        {
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
        }

        /// <summary>A wrapped line of the detail panel at <paramref name="y"/>; returns the y under it.</summary>
        private float Line(float y, string text, int size, Color color, float x = 30, float width = 920)
        {
            var t = Ui.Text(Ui.Box(_detail, x, y, width, 400), text, size, color, TextAnchor.UpperLeft);
            t.supportRichText = true;
            float h = Mathf.Ceil(t.preferredHeight) + 2;
            ((RectTransform)t.transform).sizeDelta = new Vector2(width, h);
            return y + h;
        }

        private void ShowSet(PackSet set, ShelfRow row)
        {
            var T = UiTheme.Current;
            ClearDetail();
            bool house = SelectionPacks.IsHousePack(set), import = ImportShelf.IsImport(set), real = !house && !import;
            bool rarity = real && PackCatalog.IsRaritySet(set), featured = row.Section == ShelfSection.Featured && real;

            Ui.Text(Ui.Box(_detail, 30, 20, 920, 42), set.Info.Name, 28, row.Locked ? T.TextMuted : T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            var about = house ? _state.HouseAbout(set) : "";
            var sub = house ? $"{(about.Length > 0 ? about + " · " : "")}{set.CardCount} cards · {PackCatalog.PackSize} cards per pack"
                : import ? $"OCG import · cards printed in Japan and Asia that the TCG never released · {set.CardCount} cards · {PackCatalog.PackSize} cards per pack"
                : $"{set.Info.Code} · released {set.Info.Date} · {set.CardCount} cards · {PackCatalog.PackSize} cards per pack";
            float y = Line(64, sub, 16, T.TextMuted);
            if (rarity)
            {
                // Fewer than CommonFloor commons: none at all, or a stray few drawn with the base.
                int commons = set.Pool.Where(p => p.Key == Rarity.Common || p.Key == Rarity.ShortPrint).SelectMany(p => p.Value).Distinct().Count();
                var few = commons == 0 ? "no commons" : $"almost no commons ({commons} Common{(commons == 1 ? "" : "s")}, drawn with the {Plural(PackCatalog.BaseRarity(set))})";
                y = Line(y, $"<color={T.GoldSoftHex}>A rarity set: {few}. Each pack is 7 {Plural(PackCatalog.BaseRarity(set))} and 2 foil slots.</color>", 15, T.GoldSoft);
            }
            if (row.Locked)
                y = Line(y, $"<color={T.WarnHex}>Arrives at level {row.OpensAtLevel}.</color> Look now: it goes on sale when the shop reaches {set.Year}.", 16, T.TextSoft);

            // A few of the set's best cards, as a preview.
            var best = set.Pool.OrderByDescending(p => p.Key).SelectMany(p => p.Value.Select(c => (code: c, rarity: p.Key)))
                .GroupBy(p => p.code).Select(g => g.First()).Take(5).ToList();
            var size = new Vector2(124, 180);
            float top = y + 8;
            for (int i = 0; i < best.Count; i++)
                Face(_detail, new Vector2(-292 + i * 146, 440 - (top + size.y / 2)), size, best[i].code, best[i].rarity, 0.9f);
            y = top + size.y + 12;

            // Why this set: the shelf's line, Gus's picks, your wishlist, who plays its cards, your decks; then set progress.
            var why = new List<string>();
            if (featured && _shelfLine != null) why.Add(Say(_shelfLine));
            if (featured && row.Why.Length > 0) why.Add($"<color={T.TextSoftHex}>Up front for: {row.Why}</color>");
            why.AddRange(WhyLines(set));
            if (why.Count > 0)
            {
                y = Line(y, "WHY THIS SET", 13, T.Caption);
                foreach (var w in why) y = Line(y, w, 15, T.TextBody);
            }
            if (real)
            {
                var (owned, total) = _state.SetProgress(set);
                var framed = _state.Unlocks.FramedCard(set.Info.Code, _state.Profile);
                var framedName = framed == 0 ? "a card" : _cards.Get(framed)?.Name ?? "a card";
                y = Line(y, owned >= total && total > 0
                    ? $"<color={T.GoodHex}>{owned}/{total} ✓ Set complete: Gus framed {framedName}.</color>"
                    : $"You have {owned} of its {total} cards. Finish it and Gus frames {framedName}.", 16, T.Text);
            }
            else y = Line(y, $"You have {_state.OwnedFromSet(set)} of its {set.CardCount} cards.", 16, T.Text);

            // The odds, worked out from how packs open (no guessing), and what a box gives.
            y += 8;
            if (!_oddsPicked && _state.Profile != null)
            {
                _oddsPicked = true;
                _oddsLine = _state.Voice.Pick("odds", _state.Profile, _state.Circuits, NoFill);
                _state.Save();
            }
            var caption = Ui.Text(Ui.Box(_detail, 30, y, 920, 20), "ODDS PER PACK" + (_oddsLine != null ? "   " + Say(_oddsLine) : ""), 13, T.Caption, TextAnchor.MiddleLeft);
            caption.supportRichText = true;
            y += 24;
            var odds = OddsRows(set);
            for (int i = 0; i < odds.Count; i++)
                Line(y + (i / 2) * 22, odds[i], 15, T.Text, 30 + (i % 2) * 465, 455);
            y += ((odds.Count + 1) / 2) * 22 + 4;
            bool box = featured && !row.Locked;
            if (box) y = Line(y, BoxLine(set), 15, T.TextSoft);
            Line(y, Condition(), 14, T.TextFaint);

            var buy = Ui.BoxButton(_detail, 30, 704, 440, 64, $"Buy a pack · {set.Price:N0} DP", () => Buy(set, 1), 22);
            var five = Ui.BoxButton(_detail, 500, 704, 440, 64, $"Buy 5 packs · {set.Price * 5:N0} DP", () => Buy(set, 5), 22);
            buy.targetGraphic.color = T.Primary;
            if (row.Locked)
                foreach (var b in new[] { buy, five })
                {
                    b.interactable = false;
                    // The plain fill: the disabled tint (UiTheme.Disabled) takes it to the design's ButtonOff (not ButtonOff darkened again).
                    b.targetGraphic.color = T.Button;
                    b.GetComponentInChildren<Text>().color = T.TextDim;
                }
            if (box)
            {
                int price = ShopPrices.Box(set);
                Ui.BoxButton(_detail, 30, 778, 440, 56, $"Buy a box · {ShopPrices.BoxPacks} packs · {price:N0} DP", () => BuyBox(set), 20);
                var promise = ShopPrices.BoxTop(set);
                Ui.Text(Ui.Box(_detail, 500, 778, 440, 56), promise is Rarity t ? $"At least one {CardFoil.ColoredName(t)} in every box ({ShopPrices.BoxDiscount * 100:0}% off {ShopPrices.BoxPacks} packs)"
                    : $"{ShopPrices.BoxDiscount * 100:0}% off {ShopPrices.BoxPacks} packs", 15, T.TextSoft, TextAnchor.MiddleLeft).supportRichText = true;
            }
            Ui.Text(Ui.Box(_detail, 30, 840, 920, 36),
                $"The best card is always last. You keep up to {Collection.MaxCopies} of a card: extra copies turn into DP (a rarer one replaces your least rare copy).",
                14, T.TextFaint, TextAnchor.MiddleLeft);
        }

        private static string Plural(Rarity r) => Rarities.Name(r) + "s";

        /// <summary>
        /// "Why this set", from the tags (SHOP-REWORK-SPEC section 2): "Wanted for Tearlaments (3): Havnis, Merrli, Scheiren",
        /// "Tink plays: Cyber Dragon", "In your Warriors deck (2): Marauding Captain, Command Knight". Up to 5 cards a line.
        /// </summary>
        private List<string> WhyLines(PackSet set)
        {
            if (_why.TryGetValue(set, out var cached)) return cached;
            var T = UiTheme.Current;
            var lines = new List<string>();
            if (_tags != null)
            {
                var tagged = set.Pool.Values.SelectMany(c => c).Distinct().OrderBy(c => c).Select(c => (code: c, tags: PackTags.For(c, _tags))).ToList();
                string Names(IEnumerable<uint> codes)
                {
                    var list = codes.ToList();
                    var names = list.Take(5).Select(c => _cards.Get(c)?.Name ?? c.ToString());
                    // "(+7 more)" never breaks inside its parentheses (a no-break space).
                    return string.Join(", ", names) + (list.Count > 5 ? $" (+{list.Count - 5}\u00A0more)" : "");
                }
                var wanted = tagged.Where(t => t.tags.Wanted).Select(t => t.code).ToList();
                if (wanted.Count > 0)
                    lines.Add($"<color={T.GoldSoftHex}>Wanted for {(tagged[0].tags.WantedFor.Length > 0 ? tagged[0].tags.WantedFor : "your deck")} ({wanted.Count}):</color> {Names(wanted)}");
                // Who plays its cards, the first opponent you'll meet first.
                if (!SelectionPacks.IsHousePack(set))
                {
                    var first = PackTags.Count(set, _tags).firstPlayer;
                    var players = tagged.Where(t => t.tags.PlayedBy.Count > 0).SelectMany(t => t.tags.PlayedBy.Select(p => (player: p, t.code)))
                        .GroupBy(p => p.player).OrderBy(g => g.Key == first ? 0 : 1).ThenByDescending(g => g.Count()).ToList();
                    if (players.Count > 0)
                    {
                        var line = $"<color={T.InfoHex}>{players[0].Key} plays:</color> {Names(players[0].Select(p => p.code))}";
                        // "· Tink plays some too", "· Tink and Old Gus play some too", "· Tink, Old Gus, Ryo and more play some too".
                        var others = players.Skip(1).Take(3).Select(g => g.Key).ToList();
                        if (players.Count > 4) others.Add("more");
                        if (others.Count > 0)
                            line += $" <color={T.TextDimHex}>· {CampaignState.JoinNames(others)} play{(others.Count == 1 ? "s" : "")} some too</color>";
                        lines.Add(line);
                    }
                }
                var decks = tagged.Where(t => t.tags.InDeck.Length > 0).GroupBy(t => t.tags.InDeck).ToList();
                if (decks.Count > 0)
                {
                    var mine = decks.First();
                    lines.Add($"<color={T.InfoSoftHex}>In your {mine.Key} deck ({mine.Count()}):</color> {Names(mine.Select(t => t.code))}"
                              + (decks.Count > 1 ? $" <color={T.TextDimHex}>· and {decks.Skip(1).Sum(d => d.Count())} in your other decks</color>" : ""));
                }
            }
            return _why[set] = lines;
        }

        /// <summary>The odds rows: each rarity above Common (above the base in a rarity set), rarest last: "Ultra Rare · 15 cards · 1 in 3.0 packs".</summary>
        private static List<string> OddsRows(PackSet set)
        {
            bool rarity = IsReal(set) && PackCatalog.IsRaritySet(set);
            var floor = rarity ? PackCatalog.BaseRarity(set) : Rarity.Common;
            return PackOdds.For(set).Where(o => o.Rarity > floor && o.AtLeastOne > 0)
                .Select(o => $"{CardFoil.ColoredName(o.Rarity)} · {o.Cards} card{(o.Cards == 1 ? "" : "s")} · {PackOdds.Text(o)}").ToList();
        }

        /// <summary>"A box of 24: about 8 Ultra Rares and 3 Secret Rares, and always at least one Secret Rare."</summary>
        private static string BoxLine(PackSet set)
        {
            var top = ShopPrices.BoxTop(set);
            bool rarity = PackCatalog.IsRaritySet(set);
            var floor = rarity ? PackCatalog.BaseRarity(set) : Rarity.SuperRare;
            var counts = PackOdds.For(set).Where(o => o.Rarity > floor && ShopVoice.Tier(o.Rarity) != null)
                .Select(o => (o.Rarity, n: (int)Math.Round(PackOdds.PerBox(o)))).Where(c => c.n >= 1).Take(3)
                .Select(c => $"{c.n} {(c.n == 1 ? Rarities.Name(c.Rarity) : Plural(c.Rarity))}").ToList();
            var about = counts.Count > 0 ? "about " + CampaignState.JoinNames(counts) : "";
            var always = top is Rarity t ? $"always at least one {Rarities.Name(t)}" : "";
            var tail = about.Length > 0 && always.Length > 0 ? $"{about}, and {always}" : about + always;
            return $"A box of {ShopPrices.BoxPacks}: {(tail.Length > 0 ? tail : "the set's packs at a discount")}.";
        }

        /// <summary>"Condition: Mint 45% · Near Mint 45% · Excellent 9% · Played 1% (Super Rare and up: 40 / 48 / 10 / 2)".</summary>
        private static string Condition()
        {
            string Pct(double share) => $"{Math.Round(share * 100):0}";
            var pack = Grades.ConditionOdds(CopySource.Pack).Select(w => $"{w.word} {Pct(w.share)}%");
            var foil = Grades.ConditionOdds(CopySource.Foil).Select(w => Pct(w.share));
            return $"Condition when pulled: {string.Join(" · ", pack)} (Super Rare and up: {string.Join(" / ", foil)})";
        }

        /// <summary>A card face at a rarity, centered at a point in <paramref name="parent"/>.</summary>
        private RectTransform Face(RectTransform parent, Vector2 center, Vector2 size, uint code, Rarity rarity, float strength, CopyLook look = default)
        {
            var info = _cards.Get(code);
            var card = new FieldCard
            {
                Code = code, Type = info?.Type ?? 0, Level = info?.Level ?? 0, Attack = info?.Attack ?? 0,
                Defense = info?.Defense ?? 0, Position = 0x1, IsPublic = true,
            };
            var view = CardView.Create(parent, center, size, card, info, 0, true, null, offField: true);
            foreach (var art in view.GetComponentsInChildren<RawImage>()) CardFoil.Apply(art, info, rarity, strength, look);
            foreach (var g in view.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
            return (RectTransform)view.transform;
        }

        // ---- Buying and opening ----------------------------------------------------------------------

        private int _packsLeft;
        /// <summary>The set's progress before this pack (for the summary's "Set 42/60 (+1)").</summary>
        private (int owned, int total) _progressBefore;
        private PackSet? _openSet;

        private void Buy(PackSet set, int packs)
        {
            var T = UiTheme.Current;
            if (_rowOf.TryGetValue(set, out var row) && row.Locked) return;
            var cost = set.Price * packs;
            if (_state.Profile!.Dp < cost)
            {
                _status.text = $"<color={T.BadHex}>Not enough DP:</color> {packs} pack{(packs == 1 ? "" : "s")} cost {cost} DP, you have {_state.Profile!.Dp}. Win campaign duels for more.";
                return;
            }
            _state.Profile!.Spend(set.Sink, cost);
            _state.Save();
            _status.text = $"Bought {(packs == 1 ? "a pack" : $"{packs} packs")} of {set.Info.Name} for {cost:N0} DP.";
            _packsLeft = packs;
            OpenPack(set);
        }

        private void BuildOpening()
        {
            var T = UiTheme.Current;
            _opening = Ui.Stretched("Opening", _root, Vector2.zero, Vector2.one);
            Ui.Image(_opening, T.Glass);
            var glow = Ui.Placed("Glow", _opening, new Vector2(0, 40), new Vector2(1800, 900));
            var g = glow.gameObject.AddComponent<RawImage>();
            g.texture = BoardFx.Textures.Glow;
            g.color = new Color(0.35f, 0.45f, 0.9f, 0.18f); // theme: kept, the reveal glow is pack-opening art, not a shell surface
            g.raycastTarget = false;
            _openingTitle = Ui.Text(Ui.Box(_opening, 0, 40, 1660, 50), "", 30, T.Gold);
            _openingTitle.fontStyle = FontStyle.Bold;
            _openingUnlocks = Ui.Text(Ui.Box(_opening, 0, 92, 1660, 30), "", 18, T.GoldSoft);
            _openingUnlocks.supportRichText = true;
            // The cards are laid out on the same 1920x1080 frame as the title, summary and buttons (top-left anchored), so
            // their captions keep their room above the summary at any aspect ratio.
            _packCards = Ui.Box(_opening, 0, 0, 1920, 1080);
            // What a revealed card does: hover it.
            var infoBox = Ui.Box(_opening, 1470, 150, 420, 700);
            Ui.Image(infoBox, T.GlassSoft.WithAlpha(0.92f));
            var infoView = Ui.Stretched("View", infoBox, Vector2.zero, Vector2.one);
            infoView.offsetMin = new Vector2(16, 16);
            infoView.offsetMax = new Vector2(-16, -16);
            infoView.gameObject.AddComponent<RectMask2D>();
            var infoContent = Ui.Rect("Text", infoView);
            infoContent.anchorMin = new Vector2(0, 1);
            infoContent.anchorMax = new Vector2(1, 1);
            infoContent.pivot = new Vector2(0.5f, 1);
            infoContent.sizeDelta = Vector2.zero;
            _cardInfo = Ui.Text(infoContent, "Hover a revealed card to read it.", 17, T.TextBright, TextAnchor.UpperLeft);
            _cardInfo.supportRichText = true;
            _cardInfo.verticalOverflow = VerticalWrapMode.Overflow;
            infoContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var infoScroll = infoView.gameObject.AddComponent<ScrollRect>();
            infoScroll.viewport = infoView;
            infoScroll.content = infoContent;
            infoScroll.horizontal = false;
            infoScroll.movementType = ScrollRect.MovementType.Clamped;
            infoScroll.scrollSensitivity = 30;
            _openingSummary = Ui.Text(Ui.Box(_opening, 0, 910, 1660, 34), "", 20, T.Text); // clear of the card captions
            _openingSummary.supportRichText = true;
            _openingVoice = Ui.Text(Ui.Box(_opening, 0, 944, 1660, 32), "", 18, T.TextSoft);
            _openingVoice.supportRichText = true;
            _revealAll = Ui.BoxButton(_opening, 430, 984, 240, 56, "Reveal all", () => StartCoroutine(RevealAll()), 20).gameObject;
            _openAnother = Ui.BoxButton(_opening, 710, 984, 240, 56, "Next pack", () =>
            {
                // Prize Packs: the next one won; the shop's: the next one bought (or another).
                if (_prizing) { if (_prizeQueue.Count > 0) OpenPrize(_prizeTitle, _prizeQueue.Dequeue()); }
                else if (_openSet != null) OpenPack(_openSet);
            }, 20);
            _openAnother.targetGraphic.color = T.Primary; // the opening screen's one primary
            _openingBack = Ui.BoxButton(_opening, 990, 984, 240, 56, "Back to shop", () =>
            {
                if (_prizing) { FinishPrizes(); return; }
                BackToShop();
            }, 20);
            _opening.gameObject.SetActive(false);
        }

        /// <summary>The opening screen's buttons that are showing, side by side and centered under the cards.</summary>
        private void ArrangeButtons()
        {
            const float width = 240, gap = 40, center = 830;
            var shown = new[] { _revealAll, _openAnother.gameObject, _openingBack.gameObject }.Where(b => b.activeSelf).ToList();
            float x = center - (shown.Count * width + (shown.Count - 1) * gap) / 2;
            foreach (var b in shown)
            {
                ((RectTransform)b.transform).anchoredPosition = new Vector2(x, -984);
                x += width + gap;
            }
        }

        /// <summary>
        /// The opening screen's Back to shop. Packs bought together are paid for up front, so any not opened yet go
        /// straight into the collection (a 4th copy turns into DP as in any pack), with a line saying what they gave.
        /// </summary>
        private void BackToShop()
        {
            var T = UiTheme.Current;
            string opened = "";
            if (_showing == Showing.Pack && _packsLeft > 0 && _openSet != null && _state.Profile is Profile profile)
            {
                int packs = _packsLeft, cards = 0, news = 0, sold = 0, refund = 0;
                var (before, _) = _state.SetProgress(_openSet);
                for (; _packsLeft > 0; _packsLeft--)
                {
                    var pulls = profile.OpenPack(_openSet, _rng, out var r);
                    refund += r;
                    cards += pulls.Count;
                    news += pulls.Count(x => x.isNew);
                    sold += pulls.Count(x => !x.added.Kept);
                }
                _state.Save();
                var unlocks = _state.CheckSets();
                var (owned, total) = _state.SetProgress(_openSet);
                opened = $"Opened the other {packs} pack{(packs == 1 ? "" : "s")} of {_openSet.Info.Name} into your collection: {cards} cards, {news} new"
                         + (sold > 0 ? $", {sold} extra cop{(sold == 1 ? "y" : "ies")} sold for {refund} DP" : "")
                         + (IsReal(_openSet) && total > 0 && owned > before ? $" · Set {owned}/{total} (+{owned - before})" : "") + ".";
                if (unlocks.Length > 0) opened += $" <color={T.GoldSoftHex}>{unlocks}</color>";
            }
            _opening.gameObject.SetActive(false);
            // The collection grew: the rows' set progress and tags again.
            Restock();
            _rows = RowsNow();
            if (_selected != null) Select(_selected);
            if (opened.Length > 0) _status.text = opened;
        }

        private void OpenPack(PackSet set)
        {
            var T = UiTheme.Current;
            if (_packsLeft <= 0)
            {
                // "Next pack" after the bought ones: buy another.
                if (_state.Profile!.Dp < set.Price) { _openingSummary.text = $"<color={T.BadHex}>Not enough DP for another pack ({set.Price} DP).</color>"; return; }
                _state.Profile!.Spend(set.Sink, set.Price);
                _packsLeft = 1;
            }
            _packsLeft--;
            _openSet = set;
            _showing = Showing.Pack;
            ShopButtons();
            ClearOpening();
            _progressBefore = _state.SetProgress(set);
            _tags ??= _state.Tags();

            // Yours as soon as the pack is open; a 4th copy of a card turns into DP instead (at most half the price).
            foreach (var (pull, isNew, added) in _state.Profile!.OpenPack(set, _rng, out _refund))
                _pack.Add(new PackCard { Pull = pull, New = isNew, Added = added, Hit = PackOdds.IsHit(set, pull.Rarity), Tags = TagsOf(pull.Code) });
            _state.Save();
            // A set completed by this pack: its framed card and poster (card unlocks; saved when it pays).
            int copiesBefore = _state.Profile!.Collection.Total;
            _openingUnlocks.text = _state.CheckSets();
            _unlockCopies = _state.Profile!.Collection.Total - copiesBefore;
            if (_openingUnlocks.text.Length > 0) _status.text = $"<color={T.GoldSoftHex}>{_openingUnlocks.text}</color>";

            _openingTitle.text = $"{set.Info.Name}";
            _openingSummary.text = "Click a card to flip it.";
            _openingVoice.text = "";
            _revealAll.SetActive(true);
            _openAnother.GetComponentInChildren<Text>().text = _packsLeft > 0 ? $"Next pack ({_packsLeft} left)" : $"Another · {set.Price} DP";
            ArrangeButtons();
            DealPack();
        }

        private PackCardTags? TagsOf(uint code) => _tags == null ? null : PackTags.For(Banlist.BaseCode(code, _cards), _tags);

        // ---- Booster boxes -------------------------------------------------------------------------------

        private BoxResult? _box;

        /// <summary>
        /// A box of a featured set (<see cref="BoosterBox"/>): 24 packs at once, at least one of the set's guaranteed rarity.
        /// The opening screen shows the box's summary, then its 10 best pulls face down, the promised card last.
        /// </summary>
        private void BuyBox(PackSet set)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            int price = ShopPrices.Box(set);
            if (profile.Dp < price)
            {
                _status.text = $"<color={T.BadHex}>Not enough DP:</color> a box costs {price:N0} DP, you have {profile.Dp:N0}. Win campaign duels for more.";
                return;
            }
            _progressBefore = _state.SetProgress(set);
            _tags ??= _state.Tags();
            var box = BoosterBox.Open(profile, set, price, _rng);
            if (!box.Opened) { _status.text = $"<color={T.BadHex}>The box couldn't be opened.</color>"; return; }
            _box = box;
            _state.Save();
            int copiesBefore = profile.Collection.Total;
            var unlocks = _state.CheckSets();
            _unlockCopies = profile.Collection.Total - copiesBefore;
            _status.text = unlocks.Length > 0 ? $"<color={T.GoldSoftHex}>{unlocks}</color>" : $"Bought a box of {set.Info.Name} for {price:N0} DP.";

            _openSet = set;
            _showing = Showing.Box;
            _packsLeft = 0;
            ClearOpening();
            _refund = box.Refund;
            // The 10 best pulls, least rare first, and the box's promise last.
            int topper = box.Pulls.FindIndex(p => p.pull.Code == box.Topper.Code && p.pull.Rarity == box.Topper.Rarity);
            var best = box.Pulls.Select((p, i) => (p, i)).Where(x => x.i != topper)
                .OrderByDescending(x => x.p.pull.Rarity).ThenByDescending(x => x.p.isNew).Take(9).OrderBy(x => x.p.pull.Rarity).Select(x => x.p).ToList();
            if (topper >= 0) best.Add(box.Pulls[topper]);
            foreach (var (pull, isNew, added) in best)
                _pack.Add(new PackCard { Pull = pull, New = isNew, Added = added, Hit = PackOdds.IsHit(set, pull.Rarity), Tags = TagsOf(pull.Code) });
            if (topper >= 0) _pack[_pack.Count - 1].Topper = true;

            // Every pull is one of: kept as a new copy, kept in place of a less rare copy (an upgrade), or an extra copy sold,
            // so the parts add up to the box's cards.
            int news = box.Pulls.Count(p => p.isNew), kept = box.Pulls.Count(p => p.added.Kept && p.added.Replaced == null);
            int upgrades = box.Pulls.Count(p => p.added.Kept && p.added.Replaced != null);
            var sold = box.Pulls.Where(p => !p.added.Kept).ToList();
            var progress = _state.SetProgress(set);
            _openingTitle.text = $"{set.Info.Name} · Booster Box";
            _openingUnlocks.text = unlocks;
            _openingSummary.text = $"{box.Pulls.Count} cards · {news} new · {kept} copies kept"
                                   + (upgrades > 0 ? $" · {upgrades} upgrade{(upgrades == 1 ? "" : "s")}" : "")
                                   + (sold.Count > 0 ? $" · {sold.Count} extra cop{(sold.Count == 1 ? "y" : "ies")} sold for {box.Refund} DP"
                                                     + (box.Refund < sold.Sum(p => p.added.Dp) ? " (at most half the box's price)" : "") : "")
                                   + SetLine(set, progress)
                                   + $" · You have <color={T.GoldHex}>{profile.Dp:N0} DP</color>";
            _openingVoice.text = Say(_state.Voice.Pick("box", profile, _state.Circuits, NoFill));
            _state.Save();
            _revealAll.SetActive(true);
            _openAnother.gameObject.SetActive(false);
            ArrangeButtons();
            DealPack();
        }

        /// <summary>" · Set 42/60 (+1)" for a real set (empty otherwise).</summary>
        private string SetLine(PackSet set, (int owned, int total) now)
        {
            if (!IsReal(set) || now.total == 0) return "";
            int gained = now.owned - _progressBefore.owned;
            return $" · Set {now.owned}/{now.total}" + (gained > 0 ? $" <color={UiTheme.Current.GoodHex}>(+{gained})</color>" : "");
        }

        /// <summary>The opening screen emptied for the next pack.</summary>
        private void ClearOpening()
        {
            StopAllCoroutines();
            _opening.gameObject.SetActive(true);
            _opening.SetAsLastSibling();
            for (int i = _packCards.childCount - 1; i >= 0; i--) Destroy(_packCards.GetChild(i).gameObject);
            _pack.Clear();
            _cursor = -1;
            _cardMark = null; // went with the cards
            _summarized = false;
            _openingVoice.text = "";
            _cardInfo.text = "Hover a revealed card to read it.";
        }

        /// <summary>Lays <see cref="_pack"/> out face down and deals it in.</summary>
        private void DealPack()
        {
            // Two rows: 5 then the rest, the best cards at the end; left of center, clear of the card info box on the right.
            // A pack of five (a Prize Pack) is one row, a little lower. Under each card: its rarity, name and why it matters.
            var size = _pack.Count <= 5 ? new Vector2(184, 268) : new Vector2(172, 250);
            for (int i = 0; i < _pack.Count; i++)
            {
                var card = _pack[i];
                int row = i < 5 ? 0 : 1, col = i < 5 ? i : i - 5, inRow = row == 0 ? Math.Min(5, _pack.Count) : _pack.Count - 5;
                var center = new Vector2((col - (inRow - 1) / 2f) * 220 - 130, row == 0 ? (_pack.Count <= 5 ? 60 : 226) : -152);
                card.Slot = Ui.Placed("Slot", _packCards, center, size);
                if (card.Hit)
                {
                    // Hits glow through the back before they're turned.
                    var glow = Ui.Placed("Glow", card.Slot, Vector2.zero, size * 1.9f).gameObject.AddComponent<RawImage>();
                    glow.texture = BoardFx.Textures.Glow;
                    glow.color = WithAlpha(RarityGlow(card.Pull.Rarity), 0.35f);
                    glow.raycastTarget = false;
                    card.Glow = glow;
                }
                card.Back = Ui.Stretched("Back", card.Slot, Vector2.zero, Vector2.one);
                // The player's card back picture, or the drawn one (navy, gold border and star) the duel uses too.
                CardView.DrawBack(card.Back);
                var flip = card;
                Ui.Clickable(card.Back, () => StartCoroutine(Reveal(flip)), hover: () => PointCard(flip));
                card.Slot.localScale = Vector3.one * 0.6f;
            }
            StartCoroutine(DealIn());
        }

        private IEnumerator DealIn()
        {
            Sounds.Play("pack_tear");
            for (int i = 0; i < _pack.Count; i++)
            {
                var slot = _pack[i].Slot;
                StartCoroutine(Tween(0.22f, t => { if (slot != null) slot.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, EaseOutBack(t)); }));
                yield return new WaitForSecondsRealtime(0.05f);
            }
        }

        private IEnumerator RevealAll()
        {
            foreach (var card in _pack.ToList())
            {
                if (card.Revealed) continue;
                StartCoroutine(Reveal(card));
                yield return new WaitForSecondsRealtime(card.Hit && card.Pull.Rarity >= Rarity.UltraRare ? 0.7f : 0.16f);
            }
        }

        private IEnumerator Reveal(PackCard card)
        {
            if (card.Revealed) yield break;
            card.Revealed = true;
            bool rare = card.Hit;
            bool big = rare && card.Pull.Rarity >= Rarity.UltraRare;

            // Anticipation: the glow swells before a rare card turns.
            if (big && card.Glow != null)
            {
                var glow = card.Glow;
                yield return Tween(0.35f, t =>
                {
                    glow.rectTransform.localScale = Vector3.one * (1 + 0.25f * t);
                    glow.color = WithAlpha(RarityGlow(card.Pull.Rarity), 0.35f + 0.5f * t);
                    card.Slot.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 40) * 2.5f * t);
                });
                card.Slot.localRotation = Quaternion.identity;
            }

            // Turn over: squash the back to nothing, swap in the face, and unsquash.
            Sounds.Play("flip", 0.8f);
            yield return Tween(0.12f, t => card.Slot.localScale = new Vector3(1 - t, 1 + 0.05f * t, 1));
            card.Back.gameObject.SetActive(false);
            // The copy you got: its rarity and its condition (a sold 4th copy shows plain).
            // A Prize Pack's slab shows as one, in its case, whatever copy the collection kept.
            var look = card.Added.Copy?.Look ?? default;
            if (card.Slab > 0) look = new CopyLook(card.Pull.Rarity, card.Slab, card.Added.Copy?.Seed ?? 0, card.Slab);
            var face = Face(card.Slot, Vector2.zero, card.Slot.sizeDelta, card.Pull.Code, card.Pull.Rarity, 1f, look);
            if (card.Slab > 0)
            {
                SlabView.Rim(card.Slot, Vector2.zero, card.Slot.sizeDelta);
                SlabView.RimGlass(card.Slot, Vector2.zero, card.Slot.sizeDelta, face);
            }
            card.Face = face;
            var info = _cards.Get(card.Pull.Code);
            Label(card, info);
            var pulled = card.Pull;
            var grade = card.Slab > 0 ? card.Slab : card.Added.Copy?.Shown ?? 0;
            bool slab = card.Slab > 0;
            var tags = card.Added.Kept ? card.Tags : null;
            // A pull printing an original text reads as that text in the side panel (its texts.txt entry), as it plays.
            var read = card.Added.Copy is CardCopy own && CardDetail.OriginalRow(own) is TextRow row ? _cards.Get(row.Code) ?? info : info;
            card.Read = () => ShowCardInfo(read, pulled.Rarity, grade, slab, tags);
            Ui.Clickable(card.Slot, null, hover: () => ReadCard(card));
            ReadCard(card);
            var foil = face.GetComponentInChildren<CardFoil>();
            if (foil != null) foil.Glint();
            Spoken(card);
            yield return Tween(0.14f, t => card.Slot.localScale = new Vector3(t, 1.05f - 0.05f * t, 1));

            if (rare) Sounds.Play(big ? "burst" : "rare", big ? 1f : 0.7f);
            if (rare && card.Glow != null)
            {
                var glow = card.Glow;
                var color = RarityGlow(card.Pull.Rarity);
                yield return Tween(big ? 0.6f : 0.35f, t =>
                {
                    glow.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, big ? 1.7f : 1.3f, t);
                    glow.color = WithAlpha(color, Mathf.Lerp(0.95f, 0.4f, t));
                    card.Slot.localScale = Vector3.one * (1 + (big ? 0.08f : 0.04f) * Mathf.Sin(t * Mathf.PI));
                });
            }
            // Every card is marked face up as its turn starts, so the first reveal to finish after the last one started sums up.
            if (_pack.All(c => c.Revealed) && !_summarized) { _summarized = true; Summarize(); }
        }

        /// <summary>A line as a card turns over: the box's promise (box-best), a Prize Pack's slab or Secret Rare (prize-pull, once a bracket).</summary>
        private void Spoken(PackCard card)
        {
            var profile = _state.Profile;
            if (profile == null) return;
            VoiceLine? line = null;
            if (_showing == Showing.Box && card.Topper) line = _state.Voice.Pick("box-best", profile, _state.Circuits, NoFill);
            else if (_showing == Showing.Prize && !_prizeContraband && (card.Slab > 0 || card.Pull.Rarity >= Rarity.SecretRare)) line = _state.Voice.PrizePull(profile, _state.Circuits, _prizeSeed);
            if (line == null) return;
            _openingVoice.text = Say(line);
            _state.Save();
        }

        private void Label(PackCard card, CardInfo? info)
        {
            var T = UiTheme.Current;
            var rarity = Ui.Text(Ui.Placed("Rarity", card.Slot, new Vector2(0, -card.Slot.sizeDelta.y / 2 - 16), new Vector2(260, 24)),
                CardFoil.ColoredName(card.Pull.Rarity), 15, T.Text);
            rarity.supportRichText = true;
            rarity.fontStyle = FontStyle.Bold;
            // Long names wrap to two lines within the card's width instead of running into the next card. The box is as tall
            // as the name's lines, so the tag line under it sits right below the name, one line or two.
            float bottom = card.Slot.sizeDelta.y / 2, nameTop = bottom + 28, nameWidth = card.Slot.sizeDelta.x + 16;
            var name = Ui.Text(Ui.Placed("Name", card.Slot, new Vector2(0, -nameTop - 15), new Vector2(nameWidth, 30)),
                info?.Name ?? card.Pull.Code.ToString(), 12, T.TextBody, TextAnchor.UpperCenter);
            float nameHeight = Mathf.Clamp(Mathf.Ceil(name.preferredHeight), 14, 30);
            ((RectTransform)name.transform).sizeDelta = new Vector2(nameWidth, nameHeight);
            ((RectTransform)name.transform).anchoredPosition = new Vector2(0, -nameTop - nameHeight / 2);
            // Why it matters, one line under the name (a kept copy only): your wishlist, who plays it, your deck.
            if (card.Added.Kept && card.Tags is PackCardTags tags && PackTags.Line(tags) is { Length: > 0 } why)
            {
                var color = tags.Wanted ? T.GoldSoft : tags.PlayedBy.Count > 0 ? T.Info : T.InfoSoft;
                var line = Ui.Text(Ui.Placed("Why", card.Slot, new Vector2(0, -nameTop - nameHeight - 9), new Vector2(card.Slot.sizeDelta.x + 30, 16)),
                    why, 12, color);
                line.fontStyle = FontStyle.Bold;
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 9;
                line.resizeTextMaxSize = 12;
            }
            string? tag = null;
            var badge = Ui.Hex("#E8433F");
            if (card.New) tag = "NEW";
            // A 4th copy turned into DP (a Prize Pack's pays none: it's just not kept).
            else if (!card.Added.Kept) { tag = card.Added.Dp > 0 ? $"+{card.Added.Dp} DP" : "4TH COPY"; badge = Ui.Hex("#B8860B"); }
            else if (card.Added.Replaced != null) { tag = "UPGRADE"; badge = Ui.Hex("#2E8B57"); }
            // The copy's condition, top left (glyph and word, never color alone); every pull is Raw until it's graded.
            if (card.Added.Copy is CardCopy copy)
            {
                var band = Ui.Hex(Grades.Color(copy.Shown));
                // Above the card's top edge, clear of the name a card with no art prints there.
                var chip = Ui.Placed("Condition", card.Slot, new Vector2(-card.Slot.sizeDelta.x / 2 + 48 - 8, card.Slot.sizeDelta.y / 2 + 15), new Vector2(96, 24));
                Ui.Image(chip, band).raycastTarget = false;
                var inner = Ui.Placed("Fill", chip, Vector2.zero, new Vector2(94, 22));
                Ui.Image(inner, T.GlassSoft.WithAlpha(0.92f)).raycastTarget = false;
                var word = Ui.Text(inner, $"{Grades.Glyph(copy.Shown)} {Grades.Word(copy.Shown)}", 13, band);
                word.fontStyle = FontStyle.Bold;
                word.resizeTextForBestFit = true;
                word.resizeTextMinSize = 9;
                word.resizeTextMaxSize = 13;
            }
            // A pull printing an original text (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 4.2: old sets' printings): a gold band
            // over the card's text box, "ORIGINAL TEXT · 2002", clear of the stats line at its foot (the lines under the card
            // stay as they were).
            if (card.Added.Copy is CardCopy printed && CardDetail.CarriesOriginal(printed))
            {
                var year = printed.Printed.Length >= 4 ? $" · {printed.Printed.Substring(0, 4)}" : "";
                var band = Ui.Placed("Original", card.Slot, new Vector2(0, -card.Slot.sizeDelta.y / 2 + 52), new Vector2(card.Slot.sizeDelta.x - 6, 22));
                Ui.Image(band, T.GlassSoft.WithAlpha(0.9f)).raycastTarget = false;
                var words = Ui.Text(band, $"ORIGINAL TEXT{year}", 12, T.GoldSoft);
                words.fontStyle = FontStyle.Bold;
                words.resizeTextForBestFit = true;
                words.resizeTextMinSize = 8;
                words.resizeTextMaxSize = 12;
                words.raycastTarget = false;
            }
            if (tag != null)
            {
                var width = tag.Length > 4 ? 96 : 54;
                var box = Ui.Placed("Tag", card.Slot, new Vector2(card.Slot.sizeDelta.x / 2 - width / 2f + 8, card.Slot.sizeDelta.y / 2 + 15), new Vector2(width, 24));
                Ui.Image(box, badge).raycastTarget = false;
                Ui.Text(box, tag, 14, T.Text).fontStyle = FontStyle.Bold;
            }
        }

        private int _refund;
        private Text _cardInfo = null!;
        /// <summary>The pack shown has been summed up (and its pull line picked: once a pack).</summary>
        private bool _summarized;

        /// <summary>Shows what a card does in the pack-opening info panel, and every tag of a kept copy.</summary>
        private void ShowCardInfo(CardInfo? info, Rarity rarity, int grade = 0, bool slab = false, PackCardTags? tags = null)
        {
            if (info == null) return;
            var T = UiTheme.Current;
            var kind = (info.Type & CardType.Monster) != 0
                ? ((info.Type & CardType.Link) != 0 ? $"Link Monster · ATK {CardDetail.Stat(info.Attack)}"
                    : $"Monster · Level/Rank {info.Level & 0xff} · ATK {CardDetail.Stat(info.Attack)} / DEF {CardDetail.Stat(info.Defense)}")
                : (info.Type & CardType.Spell) != 0 ? "Spell" : "Trap";
            // A slab reads as its label ("Sealed slab · 10 Gem Mint"); a Raw copy as its condition.
            var condition = grade <= 0 ? ""
                : slab ? $"\n<color={Grades.Color(grade)}>{Grades.Glyph(grade)} Sealed slab · {Grades.Label(grade)}: {Grades.Flavor(grade, raw: false)}</color>"
                : $"\n<color={Grades.Color(grade)}>{Grades.Glyph(grade)} {Grades.Condition(grade)}</color>";
            var why = new List<string>();
            if (tags != null)
            {
                if (tags.Wanted) why.Add($"<color={T.GoldSoftHex}>Wanted{(tags.WantedFor.Length > 0 ? $" for {tags.WantedFor}" : "")}</color>");
                if (tags.PlayedBy.Count > 0) why.Add($"<color={T.InfoHex}>{CampaignState.JoinNames(tags.PlayedBy)} play{(tags.PlayedBy.Count == 1 ? "s" : "")} this</color>");
                if (tags.InDeck.Length > 0) why.Add($"<color={T.InfoSoftHex}>In {tags.InDeck}</color>");
            }
            _cardInfo.text = $"<b><size=22>{info.Name}</size></b>\n{CardFoil.ColoredName(rarity)}{condition}" + (why.Count > 0 ? "\n" + string.Join("\n", why) : "") + $"\n{kind}\n\n{info.Text}";
        }

        private void Summarize()
        {
            var T = UiTheme.Current;
            // Nothing left to reveal.
            _revealAll.SetActive(false);
            ArrangeButtons();
            if (_showing == Showing.Prize)
            {
                // The pack face up: what it gave (its lines came as it turned).
                var slab = _pack.FirstOrDefault(c => c.Slab > 0);
                int fresh = _pack.Count(c => c.New);
                _openingSummary.text = $"{_pack.Count} cards · {fresh} new"
                                       + (slab != null ? $" · sealed slab: {CardFoil.ColoredName(slab.Pull.Rarity)} {_cards.Get(slab.Pull.Code)?.Name}, Grade {slab.Slab}" : "")
                                       + " · prize cards are for your binder and your decks";
                return;
            }
            if (_showing != Showing.Pack || _openSet == null) return; // a box's summary was up from the start
            var set = _openSet;
            var best = _pack.OrderByDescending(c => c.Pull.Rarity).First();
            var news = _pack.Count(c => c.New);
            var sold = _pack.Where(c => c.Added.Dp > 0).ToList();
            _openingSummary.text = $"{_pack.Count} cards · {news} new · best pull: {CardFoil.ColoredName(best.Pull.Rarity)} {_cards.Get(best.Pull.Code)?.Name}"
                                   + (sold.Count > 0 ? $" · {sold.Count} extra cop{(sold.Count == 1 ? "y" : "ies")} sold for {_refund} DP"
                                                     + (_refund < sold.Sum(c => c.Added.Dp) ? " (at most half the pack's price)" : "") : "")
                                   + SetLine(set, _state.SetProgress(set))
                                   + $" · You have <color={T.GoldHex}>{_state.Profile!.Dp:N0} DP</color>";
            // At most one line a pack, by the pull rules: a big pull, an opponent's key card, a recipe card, a deck card.
            var moment = new PackMoment
            {
                Set = set, Best = best.Pull.Rarity, BestCode = best.Pull.Code,
                HousePack = SelectionPacks.IsHousePack(set), PreviousHadLine = _lastPackHadLine,
            };
            foreach (var c in _pack.Where(c => c.Added.Kept && c.Tags != null))
            {
                var tags = c.Tags!;
                if (tags.Key && moment.OpponentKey == 0) { moment.OpponentKey = c.Pull.Code; moment.OpponentId = tags.KeyOf; }
                if (tags.Wanted && moment.RecipeCard == 0) moment.RecipeCard = c.Pull.Code;
                if ((c.New || c.Added.Replaced != null) && tags.InDeck.Length > 0 && moment.DeckCard == 0) moment.DeckCard = c.Pull.Code;
            }
            var line = _state.Voice.Pull(moment, _state.Profile!, _state.Circuits);
            _lastPackHadLine = line != null;
            _openingVoice.text = Say(line);
            _state.Save();
        }

        private static Color RarityGlow(Rarity r) => r switch
        {
            >= Rarity.QuarterCenturySecretRare => new Color(1f, 0.55f, 0.95f),
            >= Rarity.SecretRare => new Color(0.9f, 0.5f, 1f),
            >= Rarity.UltraRare => new Color(1f, 0.82f, 0.3f),
            _ => new Color(0.5f, 0.8f, 1f),
        };

        private static IEnumerator Tween(float duration, Action<float> step)
        {
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                step(Mathf.Clamp01(t / duration));
                yield return null;
            }
            step(1);
        }

        private static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

        private static float EaseOutBack(float t)
        {
            const float c = 1.70158f;
            return 1 + (c + 1) * Mathf.Pow(t - 1, 3) + c * Mathf.Pow(t - 1, 2);
        }

        // ---- Prize Packs (Brackets) --------------------------------------------------------------------

        /// <summary>Prize Packs being revealed (the opening screen's buttons then lead through them, then <see cref="_prizeDone"/>).</summary>
        private bool _prizing;
        private readonly Queue<List<PrizePull>> _prizeQueue = new Queue<List<PrizePull>>();
        private string _prizeTitle = "", _prizeUnlocks = "";
        private int _prizeCount, _prizeNumber;
        /// <summary>The finished bracket's seed: Hollis's prize-pull line plays once per bracket.</summary>
        private uint _prizeSeed;
        /// <summary>The prizes are an alley bracket's Contraband Packs (Moth's word on them, not Hollis's; their own slab odds).</summary>
        private bool _prizeContraband;
        /// <summary>The alley packs' name on the reveal ("Contraband Pack"; the Invitational's "No Limits Pack").</summary>
        private string _prizePackName = "Contraband Pack";
        private Action? _prizeDone;
        private Button _openingBack = null!;

        /// <summary>
        /// Reveals a finished bracket's Prize Packs (<see cref="Brackets.Finish"/> already put their cards in your collection)
        /// on the pack-opening screen, one after another ("Next pack (n left)"), then <paramref name="done"/>. Set completion
        /// is checked first: the prizes may have finished one. <paramref name="bracketSeed"/>: the bracket's (for Hollis's line).
        /// <paramref name="contraband"/>: an alley bracket's Contraband Packs (<see cref="ContrabandPacks"/>): named so, with
        /// their slab odds, and Moth's line (van:pack) over the first in place of Hollis's. <paramref name="packName"/>: what
        /// they're called on the reveal (the Invitational's No Limits Packs: the same pack over another pool); "" for "Contraband Pack".
        /// </summary>
        public void OpenPrizes(string title, IReadOnlyList<List<PrizePull>> packs, Action done, uint bracketSeed = 0, bool contraband = false, string packName = "")
        {
            _prizeContraband = contraband;
            _prizePackName = packName.Length > 0 ? packName : "Contraband Pack";
            _prizeQueue.Clear();
            foreach (var pack in packs) _prizeQueue.Enqueue(pack);
            _prizeTitle = title;
            _prizeCount = packs.Count;
            _prizeNumber = 0;
            _prizeDone = done;
            _prizeSeed = bracketSeed;
            _prizing = true;
            // This bracket's tags (the shop's may be from an earlier visit, before this cup's wins and wishlist).
            _tags = _state.Tags();
            if (_prizeQueue.Count == 0) { FinishPrizes(); return; }
            int copiesBefore = _state.Profile?.Collection.Total ?? 0;
            _prizeUnlocks = _state.CheckSets();
            OpenPrize(title, _prizeQueue.Dequeue());
            _unlockCopies = (_state.Profile?.Collection.Total ?? 0) - copiesBefore;
        }

        /// <summary>
        /// One Prize Pack on the pack-opening screen: its five pulls face down, least rare first and the sealed slab last
        /// (<see cref="PrizePacks.RevealOrder"/>). Its cards are already yours; Done (or Next pack) moves on. The subtitle
        /// gives the slab's odds ("Prize Pack 1 of 2 · slab: Grade 9 75%, Grade 10 25%").
        /// </summary>
        public void OpenPrize(string title, List<PrizePull> pulls)
        {
            var T = UiTheme.Current;
            _prizing = true;
            _showing = Showing.Prize;
            _root.gameObject.SetActive(true);
            ClearOpening();
            _refund = 0;
            _unlockCopies = 0;
            _tags ??= _state.Tags();
            foreach (var pull in pulls)
                _pack.Add(new PackCard
                {
                    Pull = new Pull(pull.Code, pull.Rarity), New = pull.IsNew, Added = new Added(pull.Kept, pull.Replaced, 0, pull.Copy), Slab = pull.Slab,
                    Hit = pull.Rarity >= Rarity.SuperRare || pull.Slab > 0, Tags = TagsOf(pull.Code),
                });
            _prizeNumber++;
            _openingTitle.text = title;
            int ten = _prizeContraband ? (int)Math.Round((1 - AlleyEconomy.PackSlabNine) * 100) : PrizePacks.SlabTenPercent;
            var slab = $"slab: Grade 9 {100 - ten}%, Grade 10 {ten}%";
            _openingUnlocks.text = $"{(_prizeContraband ? _prizePackName : "Prize Pack")} {_prizeNumber} of {_prizeCount} · {slab}" + (_prizeNumber == 1 && _prizeUnlocks.Length > 0 ? "  ·  " + _prizeUnlocks : "");
            // Moth sealed them: his word on the first one (Contraband Packs are only ever bracket prizes).
            if (_prizeContraband && _prizeNumber == 1 && _state.Profile is Profile owner
                && _state.VanVoice.Pick("van:pack", owner, _state.Circuits, NoFill) is VoiceLine moth)
            {
                _openingVoice.text = moth.IsLabel ? moth.Text : $"<color={T.GoldSoftHex}>{_state.CastName(moth.Speaker)}</color>  {T.Paint($"\"{moth.Text}\"", T.Quote)}";
                _state.Save();
            }
            _openingSummary.text = "Click a card to flip it. The last one is a sealed slab.";
            _revealAll.SetActive(true);
            bool more = _prizeQueue.Count > 0;
            _openAnother.gameObject.SetActive(more);
            if (more) _openAnother.GetComponentInChildren<Text>().text = $"Next pack ({_prizeQueue.Count} left)";
            _openingBack.GetComponentInChildren<Text>().text = "Done";
            // One primary: the next pack while there is one, then Done.
            _openingBack.targetGraphic.color = more ? T.Button : T.Primary;
            ArrangeButtons();
            DealPack();
        }

        /// <summary>The prizes are revealed (or skipped: they're yours either way): the shop closes without its Closed, then what's next.</summary>
        private void FinishPrizes()
        {
            _prizing = false;
            _prizeQueue.Clear();
            StopAllCoroutines();
            ShopButtons();
            _opening.gameObject.SetActive(false);
            _root.gameObject.SetActive(false);
            var done = _prizeDone;
            _prizeDone = null;
            done?.Invoke();
        }

        /// <summary>The opening screen's buttons as the shop has them (after Prize Packs or a box changed them).</summary>
        private void ShopButtons()
        {
            if (_prizing) return;
            _openAnother.gameObject.SetActive(true);
            _openingBack.GetComponentInChildren<Text>().text = "Back to shop";
            _openingBack.targetGraphic.color = UiTheme.Current.Button;
            ArrangeButtons();
        }

        /// <summary>For checks: whether Prize Packs are showing, the cards of the one shown, how many are face up, and packs left.</summary>
        public (bool open, int cards, int revealed, int left) DebugPrize() =>
            (_prizing && _opening.gameObject.activeSelf, _pack.Count, _pack.Count(c => c.Revealed), _prizeQueue.Count);

        /// <summary>For checks: the prize shown's line under its title ("Contraband Pack 1 of 2 · slab: ...") and the voice line under the cards.</summary>
        public string DebugPrizeLine => $"{_openingUnlocks.text}\n{_openingVoice.text}";

        /// <summary>For checks: Reveal all on the Prize Pack shown.</summary>
        public IEnumerator DebugRevealPrize() => RevealAll();

        /// <summary>For checks: the Prize Packs' Done.</summary>
        public void DebugFinishPrizes() { if (_prizing) FinishPrizes(); }

        /// <summary>For automated play: the Prize Packs' "Next pack" (false when none is left).</summary>
        public bool DebugNextPrize()
        {
            if (!_prizing || _prizeQueue.Count == 0) return false;
            OpenPrize(_prizeTitle, _prizeQueue.Dequeue());
            return true;
        }

        // ---- The keyboard ------------------------------------------------------------------------------

        // The arrows (or the d-pad) move the highlight and Enter (or Space) acts on it (ArrowNav): the shelves' rows (up/down,
        // across pages; left/right turn the page), or a pack's cards on the opening screen (along a row and between the rows).
        // Enter on a set asks "Buy a pack of X for N DP?" (KeyConfirm: a second Enter buys through Buy, Escape cancels; one
        // press never buys); on the back shelves' row it opens or closes them; on the opening screen it turns a card over.
        // Buy 5, a box and "Another" stay clicks.
        private readonly ArrowNav _keys = new ArrowNav();
        private readonly KeyConfirm _confirm = new KeyConfirm();
        /// <summary>The pack card the keyboard has highlighted (-1: none yet this pack), and its gold frame.</summary>
        private int _cursor = -1;
        private RectTransform? _cardMark;

        private void Update()
        {
            if (!IsOpen) { _confirm.Close(); return; }
            // A clicked button keeps the UI's selection, and Enter would press it again (another pack bought).
            ArrowNav.DropButtonFocus(_root);
            if (_opening.gameObject.activeSelf) _confirm.Close();
            if (_confirm.Update()) return; // "Buy a pack?" is up: Enter buys, Escape cancels
            if (_opening.gameObject.activeSelf)
            {
                var k = _keys.Read(_opening, _opening);
                if (k.Any) PackKeys(k);
            }
            else if (_shelves != null)
            {
                var k = _keys.Read(_root, _list, except: _opening);
                if (k.Any) ShelfKeys(k);
            }
        }

        /// <summary>A row the arrows stop on: a set (a soon row too: a click shows it) and the back shelves' row.</summary>
        private bool CanHighlight(int i) => _rows[i].Kind == ShelfRowKind.Set || _rows[i].Kind == ShelfRowKind.BackToggle;

        /// <summary>The highlighted row's index in the list (-1: none shown).</summary>
        private int HighlightedRow() => _backSelected ? _rows.FindIndex(r => r.Kind == ShelfRowKind.BackToggle)
            : _selected == null ? -1 : _rows.FindIndex(r => r.Kind == ShelfRowKind.Set && r.Set == _selected);

        private void ShelfKeys(ArrowNav.Keys k)
        {
            int at = HighlightedRow();
            // An arrow goes on from the row the mouse last pointed at (ArrowNav.Point); Enter stays on the selected set.
            if (k.Move != Vector2Int.zero) at = _keys.From(at, _rows.Count);
            int to = ArrowNav.ListStep(_rows.Count, PerPage, _page, at, k.Move, CanHighlight);
            if (to >= 0)
            {
                // The highlight moves as a click would put it: the set selected (its detail shown), or the back shelves' row.
                if (_rows[to].Kind == ShelfRowKind.BackToggle) { _page = to / PerPage; ShowBack(); }
                else Select(_rows[to].Set!, page: true);
                at = to;
            }
            if (!k.Enter || at < 0 || at / PerPage != _page) return;
            if (_rows[at].Kind == ShelfRowKind.BackToggle) ToggleBack();
            else if (_rows[at].Locked) Select(_rows[at].Set!);
            else AskToBuy(_rows[at].Set!);
        }

        /// <summary>
        /// Enter on a set: "Buy a pack of X for N DP?" (KeyConfirm), the second Enter buying as the "Buy a pack" button does.
        /// Short of DP, Buy's own line says so at once (nothing to confirm).
        /// </summary>
        private void AskToBuy(PackSet set)
        {
            if (_selected != set) Select(set);
            if (_state.Profile!.Dp < set.Price) { Buy(set, 1); return; }
            _confirm.Open(_root, $"Buy a pack of <b>{set.Info.Name}</b> for {set.Price:N0} DP?", "Buy", () => Buy(set, 1));
        }

        /// <summary>For checks: the keyboard's buy question as shown ("" when none is up).</summary>
        public string DebugConfirm => _confirm.Question;

        /// <summary>The pack's cards laid out (y down), for moving between them.</summary>
        private List<Vector2> PackPoints() => _pack.Select(c => new Vector2(c.Slot.anchoredPosition.x, -c.Slot.anchoredPosition.y)).ToList();

        private void PackKeys(ArrowNav.Keys k)
        {
            if (_pack.Count == 0) return;
            if (k.Move != Vector2Int.zero)
            {
                // The first press shows the highlight where the side panel is (the first card when it reads none).
                if (_cursor < 0 || _cursor >= _pack.Count) Highlight(Math.Max(0, _pack.FindIndex(c => c.Revealed && _shownCard == c)));
                else if (ArrowNav.Step(PackPoints(), _cursor, k.Move, 40) is int next && next >= 0) Highlight(next);
            }
            if (!k.Enter) return;
            // Enter with nothing highlighted takes the next card face down.
            if (_cursor < 0 || _cursor >= _pack.Count) Highlight(Math.Max(0, _pack.FindIndex(c => !c.Revealed)));
            var card = _pack[_cursor];
            if (!card.Revealed) StartCoroutine(Reveal(card));
        }

        /// <summary>The card the side panel reads (the last turned over or hovered).</summary>
        private PackCard? _shownCard;

        /// <summary>The keyboard's highlight on a pack card: its frame, and the side panel reads it (or says it's face down).</summary>
        private void Highlight(int i)
        {
            _cursor = i;
            var card = _pack[i];
            ArrowNav.Mark(ref _cardMark, _packCards, card.Slot, 10);
            if (card.Revealed && card.Read != null) { _shownCard = card; card.Read(); }
            else
            {
                _shownCard = null;
                _cardInfo.text = $"<b><size=22>Card {i + 1} of {_pack.Count}</size></b>\nFace down. Enter (or a click) turns it over.";
            }
        }

        /// <summary>A face-up card in the side panel (turned over, or hovered); the keyboard's highlight goes with a hover.</summary>
        private void ReadCard(PackCard card)
        {
            _shownCard = card;
            card.Read?.Invoke();
            int i = _pack.IndexOf(card);
            if (i >= 0 && (_keys.Used || InputMode.Pointer))
            {
                _cursor = i;
                ArrowNav.Mark(ref _cardMark, _packCards, card.Slot, 10);
            }
        }

        /// <summary>
        /// The mouse over a face-down pack card: the keyboard's highlight moves to it, its frame hidden while the mouse is in
        /// use (KeyFrame), so the arrows go on from there. Ignored while the keys drive (a card dealt under a resting mouse).
        /// </summary>
        private void PointCard(PackCard card)
        {
            int i = _pack.IndexOf(card);
            if (i < 0 || !InputMode.Pointer) return;
            _cursor = i;
            ArrowNav.Mark(ref _cardMark, _packCards, card.Slot, 10);
        }

        // ---- Automated checks ------------------------------------------------------------------------

        /// <summary>For checks: the back shelves' row is the highlighted one.</summary>
        public bool DebugBackSelected => _backSelected;

        /// <summary>For checks: whether the opening screen (a pack, a box, Prize Packs) is up.</summary>
        public bool DebugOpeningShown => _opening.gameObject.activeSelf;

        /// <summary>For checks: the page of the shelves shown.</summary>
        public int DebugPage => _page;

        /// <summary>For checks: the opening screen's highlighted card (-1: none), whether its frame shows, and which cards are face up.</summary>
        public (int cursor, bool framed, List<bool> revealed) DebugHighlight =>
            (_cursor, _cardMark != null && _cardMark.gameObject.activeInHierarchy, _pack.Select(c => c.Revealed).ToList());

        /// <summary>For checks: a pack card's name, as the side panel reads it (an original text's name for such a copy).</summary>
        public string DebugPackName(int i)
        {
            var c = _pack[i];
            var read = c.Added.Copy is CardCopy own && CardDetail.OriginalRow(own) is TextRow row ? _cards.Get(row.Code) : null;
            return (read ?? _cards.Get(c.Pull.Code))?.Name ?? "";
        }

        /// <summary>For checks: puts the keyboard in the search field (or takes it out).</summary>
        public void DebugTypeInSearch(bool on)
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (on)
            {
                events?.SetSelectedGameObject(_search.gameObject);
                _search.ActivateInputField();
            }
            else
            {
                _search.DeactivateInputField();
                if (events != null && events.currentSelectedGameObject == _search.gameObject) events.SetSelectedGameObject(null);
            }
        }

        /// <summary>For checks: whether the search field has the keyboard.</summary>
        public bool DebugTyping => _search.isFocused;

        /// <summary>For checks: gives the UI's selection to the detail's "Buy a pack" button, as a click leaves it; false if it isn't there.</summary>
        public bool DebugFocusBuy()
        {
            var buy = _detail.GetComponentsInChildren<Button>().FirstOrDefault(b => b.GetComponentInChildren<Text>().text.StartsWith("Buy a pack"));
            if (buy == null || UnityEngine.EventSystems.EventSystem.current == null) return false;
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(buy.gameObject);
            return true;
        }

        /// <summary>Boxes bought by <see cref="DebugShop"/> (the QA stand-in buys one a run).</summary>
        public int DebugBoxes { get; private set; }

        /// <summary>
        /// For automated play: a visit as a player makes it, with real DP. The default pick (the first featured set with
        /// commons), or with <paramref name="selection"/> (decks you're rebuilding) one of their house packs if any is on
        /// sale; up to <paramref name="packs"/> packs (Buy a pack, then "Another"), never going under <paramref name="keepDp"/>,
        /// each revealed. With <paramref name="box"/>, a box of a featured pick too when DP allows (a box and the keep-DP).
        /// Logs each pack, and checks the DP and the collection against what the pack says it did (problems go to <paramref name="issue"/>).
        /// </summary>
        public IEnumerator DebugShop(int packs, int keepDp, Action<string> log, Action<string> issue, Func<string, IEnumerator> shot,
            IReadOnlyList<string>? selection = null, bool box = false)
        {
            Open(visit: !IsOpen);
            var profile = _state.Profile!;
            if (_shelves == null) { log("shop: no stock"); yield break; }
            log($"shop: visit {profile.ShopVisits}, shelf {_shelves.Shelf}: {StripTags(_status.text)}");
            var set = selection?.Select(deck => Available().FirstOrDefault(s => s.Info.Code == "SP-" + deck)).Where(s => s != null)
                .OrderByDescending(s => s!.CardCount).FirstOrDefault();
            set ??= _shelves.DefaultPick;
            if (set == null) { log("shop: nothing on sale"); yield break; }
            var row = _rowOf.TryGetValue(set, out var r) ? r : null;
            var where = SelectionPacks.IsHousePack(set) ? "a house pack" : row?.Section == ShelfSection.Featured ? $"the featured shelf ({_shelves.FeaturedHeader})"
                : row?.Section == ShelfSection.NewArrivals ? "New arrivals" : "the back shelves";
            Select(set, page: true);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return shot("shop");
            log($"shop: {set.Info.Name} ({set.Info.Code}, {set.Info.Date}, {set.CardCount} cards) at {set.Price} DP from {where}; {profile.Dp} DP, {profile.Collection.Total} cards");
            if (profile.Wanted().Count > 0)
            {
                var wanted = profile.Wanted();
                var top = Available().OrderByDescending(s => PackCatalog.Expected(s, wanted, 60)).FirstOrDefault();
                if (top != null) log($"shop: the wishlist's best set would be {top.Info.Name} ({profile.Wanted().Count} cards wanted for {profile.WishlistFor})");
            }
            for (int p = 0; p < packs && profile.Dp - set.Price >= keepDp; p++)
            {
                int dp = profile.Dp, total = profile.Collection.Total;
                if (p == 0) Buy(set, 1); else OpenPack(set); // "Buy a pack", then the opening screen's "Another"
                if (_pack.Count == 0) { issue($"bought a pack of {set.Info.Name} but it opened empty"); break; }
                yield return new WaitForSecondsRealtime(0.5f);
                yield return RevealAll();
                yield return new WaitForSecondsRealtime(1.2f);
                if (p == 0) yield return shot("pack");
                int kept = _pack.Count(c => c.Added.Kept && c.Added.Replaced == null) + _unlockCopies;
                if (profile.Dp != dp - set.Price + _refund) issue($"pack DP: had {dp}, paid {set.Price}, refund {_refund}, now {profile.Dp}");
                if (profile.Collection.Total != total + kept) issue($"pack cards: had {total}, {kept} new copies kept, now {profile.Collection.Total}");
                if (!_pack.All(c => c.Revealed)) issue("Reveal all left cards face down");
                var best = _pack.OrderByDescending(c => c.Pull.Rarity).First();
                log($"shop: pack {p + 1}: {string.Join(", ", _pack.Select(c => $"{_cards.Get(c.Pull.Code)?.Name ?? c.Pull.Code.ToString()} ({c.Pull.Rarity}{(c.New ? ", new" : "")}{(!c.Added.Kept ? ", sold" : "")})"))}"
                    + $"; best {_cards.Get(best.Pull.Code)?.Name}; {StripTags(_openingSummary.text)}" + (_openingVoice.text.Length > 0 ? $"; said: {StripTags(_openingVoice.text)}" : ""));
            }
            if (profile.Dp - set.Price < keepDp) log($"shop: stopped at {profile.Dp} DP (keeping {keepDp})");
            // A box once, of a featured set, when DP covers it and the keep-DP.
            var boxSet = row?.Section == ShelfSection.Featured && !row.Locked && IsReal(set) ? set : null;
            if (box && boxSet != null && profile.Dp >= ShopPrices.Box(boxSet) + keepDp)
            {
                int price = ShopPrices.Box(boxSet), dp = profile.Dp, total = profile.Collection.Total, opened = profile.PacksOpened;
                BuyBox(boxSet);
                var result = _box;
                if (result == null || !result.Opened) issue($"a box of {boxSet.Info.Name} ({price} DP, {dp} DP on hand) didn't open");
                else
                {
                    DebugBoxes++;
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return RevealAll();
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return shot("box");
                    int kept = result.Pulls.Count(x => x.added.Kept && x.added.Replaced == null) + _unlockCopies;
                    var topRarity = ShopPrices.BoxTop(boxSet);
                    if (profile.Dp != dp - price + result.Refund) issue($"box DP: had {dp}, paid {price}, refund {result.Refund}, now {profile.Dp}");
                    if (profile.Collection.Total != total + kept) issue($"box cards: had {total}, {kept} new copies kept, now {profile.Collection.Total}");
                    if (result.Pulls.Count != ShopPrices.BoxPacks * PackCatalog.PackSize) issue($"a box gave {result.Pulls.Count} cards");
                    if (profile.PacksOpened != opened + ShopPrices.BoxPacks) issue($"a box counted {profile.PacksOpened - opened} packs opened");
                    if (topRarity is Rarity t && result.Topper.Rarity != t) issue($"the box's promise was {t}, its topper {result.Topper.Rarity}");
                    log($"shop: a box of {boxSet.Info.Name} for {price:N0} DP: topper {_cards.Get(result.Topper.Code)?.Name} ({result.Topper.Rarity}{(result.Upgraded ? ", upgraded" : "")}); "
                        + $"{StripTags(_openingSummary.text)}" + (_openingVoice.text.Length > 0 ? $"; said: {StripTags(_openingVoice.text)}" : ""));
                }
            }
            _opening.gameObject.SetActive(false);
            Restock();
            _rows = RowsNow();
            Select(set);
            var saved = Profile.Load(_state.SavePath);
            if (saved == null || saved.Dp != profile.Dp || saved.Collection.Total != profile.Collection.Total)
                issue($"after the shop the save has {saved?.Dp} DP / {saved?.Collection.Total} cards, the game {profile.Dp} / {profile.Collection.Total}");
        }

        private static string StripTags(string text) => System.Text.RegularExpressions.Regex.Replace(text ?? "", "<.*?>", "");

        /// <summary>For checks: buys one pack of the set on sale with this name (its price added first); the cards pulled.</summary>
        public List<uint> DebugBuyPack(string name)
        {
            var set = Available().FirstOrDefault(x => x.Info.Name == name);
            if (set == null) return new List<uint>();
            Select(set, page: true);
            _state.Profile!.Earn(Ledger.Debug, set.Price);
            Buy(set, 1);
            return _pack.Select(c => c.Pull.Code).ToList();
        }

        /// <summary>For screenshots: opens a pack of a set on sale (free; 0 is the default pick's shelf's first) and reveals it.</summary>
        public IEnumerator DebugOpenPack(int setIndex, Action<string> shot)
        {
            Open(visit: false);
            var sets = Available();
            var set = setIndex == 0 && _shelves?.DefaultPick != null ? _shelves.DefaultPick : sets[Mathf.Clamp(setIndex, 0, sets.Count - 1)];
            Select(set, page: true);
            yield return new WaitForSecondsRealtime(0.6f);
            shot("shop");
            yield return null; // the screenshot is taken at the end of the frame
            _state.Profile!.Earn(Ledger.Debug, set.Price);
            Buy(set, 1);
            yield return new WaitForSecondsRealtime(0.8f);
            shot("pack-closed");
            yield return null;
            yield return RevealAll();
            yield return new WaitForSecondsRealtime(1.5f);
            shot("pack-revealed");
            yield return null;
        }

        /// <summary>For checks: the last pack's kept pulls, and how many show their condition chip.</summary>
        public (int kept, int chips) DebugConditions() =>
            (_pack.Count(c => c.Added.Copy != null), _pack.Count(c => c.Added.Copy != null && c.Slot.Find("Condition") != null));

        // For -shopcheck (DuelController.Shop).

        /// <summary>The header plate, the status bar, the set selected, this visit's shelves, and the rows shown.</summary>
        public string DebugPlate => _plate.text;
        public string DebugStatus => StripTags(_status.text);
        public PackSet? DebugSelected => _backSelected ? null : _selected;
        public ShopShelves? DebugShelves => _shelves;
        public IReadOnlyList<ShelfRow> DebugRows => _rows;

        /// <summary>Opens or closes the back shelves.</summary>
        public void DebugBack(bool open) => ToggleBack(open);

        /// <summary>Selects a set by code (on sale, a soon row, or one on the back shelves, which then open); false if it isn't in the shop.</summary>
        public bool DebugSelectCode(string code)
        {
            var row = _rowOf.Values.FirstOrDefault(r => r.Set?.Info.Code == code);
            if (row == null) return false;
            if (row.Section == ShelfSection.BackCatalog && !_backOpen) { _backOpen = true; _rows = RowsNow(); }
            Select(row.Set!, page: true);
            return true;
        }

        /// <summary>Every text in the detail panel, the list (this page) and the opening screen, tags stripped.</summary>
        public string DebugDetailText() => string.Join("\n", _detail.GetComponentsInChildren<Text>().Select(t => StripTags(t.text)));
        public string DebugListText() => string.Join("\n", _list.GetComponentsInChildren<Text>().Select(t => StripTags(t.text)));
        public string DebugOpeningText() => string.Join("\n", _opening.GetComponentsInChildren<Text>().Select(t => StripTags(t.text)));

        /// <summary>The odds rows shown for a set (rarity names first).</summary>
        public List<string> DebugOddsRows(PackSet set) => OddsRows(set).Select(StripTags).ToList();

        /// <summary>The last pack's (or box's) tag lines under the names, its summary and its line.</summary>
        public List<string> DebugTagLines() => _pack.Where(c => c.Slot != null).Select(c => c.Slot.Find("Why")?.GetComponentInChildren<Text>()?.text ?? "").Where(t => t.Length > 0).ToList();
        public string DebugSummary => StripTags(_openingSummary.text);
        public string DebugVoice => StripTags(_openingVoice.text);
        public BoxResult? DebugBox => _box;
        public int DebugRefund => _refund;
        public int DebugUnlockCopies => _unlockCopies;
        /// <summary>For checks: the reveal's side panel text (no markup).</summary>
        public string DebugCardInfo => System.Text.RegularExpressions.Regex.Replace(_cardInfo.text, "<.*?>", "");

        /// <summary>For checks: the reveal's ORIGINAL TEXT tags (the face-up cards' bands), and the kept copies with an original text.</summary>
        public (int tags, int originals) DebugOriginalTags =>
            (_pack.Count(c => c.Slot != null && c.Slot.Find("Original") != null), _pack.Count(c => c.Added.Copy is CardCopy k && CardDetail.CarriesOriginal(k)));

        public IReadOnlyList<(uint code, bool kept, bool upgrade)> DebugPack => _pack.Select(c => (c.Pull.Code, c.Added.Kept, c.Added.Replaced != null)).ToList();

        /// <summary>Buys (its price added first) and reveals one pack of a set with a seeded rng, or a box of it.</summary>
        public IEnumerator DebugSeeded(string code, int seed, bool box)
        {
            var set = Available().FirstOrDefault(s => s.Info.Code == code);
            if (set == null) yield break;
            Select(set, page: true);
            _rng = new System.Random(seed);
            if (box) BuyBox(set);
            else
            {
                _state.Profile!.Earn(Ledger.Debug, set.Price);
                Buy(set, 1);
            }
            _rng = new System.Random();
            yield return new WaitForSecondsRealtime(0.8f);
        }

        /// <summary>Reveal all on what's shown, then a moment for the last glow.</summary>
        public IEnumerator DebugRevealAll()
        {
            yield return RevealAll();
            yield return new WaitForSecondsRealtime(1.4f);
        }

        /// <summary>The header's Recipe Book button.</summary>
        public void DebugRecipeBook() => RecipeClicked?.Invoke();

        /// <summary>The opening screen's Back to shop.</summary>
        public void DebugBackToShop()
        {
            if (!_opening.gameObject.activeSelf || _prizing) return;
            BackToShop();
        }

        /// <summary>For checks: buys <paramref name="packs"/> packs of a set on sale by code (their price added first), as "Buy 5 packs" does.</summary>
        public bool DebugBuyPacks(string code, int packs)
        {
            var set = Available().FirstOrDefault(s => s.Info.Code == code);
            if (set == null) return false;
            Select(set, page: true);
            _state.Profile!.Earn(Ledger.Debug, set.Price * packs);
            Buy(set, packs);
            return _opening.gameObject.activeSelf;
        }

        /// <summary>For checks: packs bought and not opened yet.</summary>
        public int DebugPacksLeft => _packsLeft;

        /// <summary>
        /// For checks, on the opening screen's frame (y up): the widest gap between a card's name and its tag line, and the
        /// least room under any card's captions to what's below them (a lower card's chips, else the summary's top).
        /// </summary>
        public (float nameToTag, float room) DebugCaptions()
        {
            float gap = 0, room = float.MaxValue;
            var slots = _pack.Where(c => c.Slot != null).ToList();
            foreach (var c in slots)
            {
                float slotY = c.Slot.anchoredPosition.y;
                if (!(c.Slot.Find("Name") is RectTransform name)) continue;
                float bottom = slotY + name.anchoredPosition.y - name.sizeDelta.y / 2;
                if (c.Slot.Find("Why") is RectTransform why)
                {
                    gap = Mathf.Max(gap, bottom - (slotY + why.anchoredPosition.y + why.sizeDelta.y / 2));
                    bottom = slotY + why.anchoredPosition.y - why.sizeDelta.y / 2;
                }
                float next = slots.Where(o => o.Slot.anchoredPosition.y < slotY - 1).Select(o => o.Slot.anchoredPosition.y + o.Slot.sizeDelta.y / 2 + 27)
                    .DefaultIfEmpty(540f - 910f).Max();
                room = Mathf.Min(room, bottom - next);
            }
            return (gap, room);
        }

        /// <summary>For checks: the opening screen's buttons showing (labels), and how far their group's middle is from the cards' middle.</summary>
        public (List<string> shown, float offCenter) DebugButtons()
        {
            var shown = new[] { _revealAll, _openAnother.gameObject, _openingBack.gameObject }.Where(b => b.activeSelf).ToList();
            if (shown.Count == 0) return (new List<string>(), 0);
            float left = shown.Min(b => ((RectTransform)b.transform).anchoredPosition.x), right = shown.Max(b => ((RectTransform)b.transform).anchoredPosition.x + 240);
            return (shown.Select(b => b.GetComponentInChildren<Text>().text).ToList(), Mathf.Abs((left + right) / 2 - 830));
        }
    }
}
