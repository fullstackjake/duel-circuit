using System;
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
    /// The Recipe Book (Tag Force's deck recipes): every duelist's deck in campaign order, grouped by map region. Beating
    /// a duelist while they play a deck unlocks it. An unlocked recipe shows the full list, its story (when it was
    /// played, how it worked, why it mattered), how much of it you own, the cards you're missing and where to get them,
    /// and builds a deck of yours from it. Locked recipes say who to beat, and where.
    /// </summary>
    public sealed partial class RecipeBookScreen : MonoBehaviour
    {
        private const float Row = 82;

        private CampaignState _state = null!;
        private DeckLibrary _library = null!;
        private ICardDataProvider _cards = null!;
        private RectTransform _root = null!, _list = null!, _detail = null!;
        private ScrollRect _scroll = null!;
        private Text _header = null!, _status = null!;
        private List<Recipe> _recipes = new List<Recipe>();
        private int _selected;
        private bool _scrollToSelected;
        private float _rowTop;

        public event Action? Closed;
        /// <summary>Build was pressed: a new campaign deck of this name was saved; open it in the editor.</summary>
        public event Action<string>? EditDeck;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        /// <summary>Watch a Replay Lesson of a recipe (the controller prepares it and calls back with progress).</summary>
        public event Action<Recipe>? WatchLesson;

        /// <summary>Shows a line in the status bar (lesson progress).</summary>
        public void SetStatus(string text) => _status.text = text;

        public void Init(CampaignState state, DeckLibrary library, ICardDataProvider cards)
        {
            _state = state;
            _library = library;
            _cards = cards;
            Build();
            _root.gameObject.SetActive(false);
        }

        private Profile Profile => _state.Profile!;
        private Circuits Circuits => _state.Circuits;

        private void Build()
        {
            // Over the shop (14), like the shop's other screens (the Trader, the Grading Desk, the Structure Decks): at the
            // shop's own 14 the shop drew over it, so the shop's Recipe Book button opened it out of sight.
            _root = Ui.Overlay(transform, "RecipeBook", 15);
            Ui.Image(_root, Ui.Hex("#121620"));
            Ui.Image(Ui.Box(_root, 0, 0, 1920, 90), Ui.Hex("#0D1017"));
            Ui.Text(Ui.Box(_root, 40, 0, 400, 90), "RECIPE BOOK", 34, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _header = Ui.Text(Ui.Box(_root, 380, 0, 1200, 90), "", 20, Ui.Hex("#FFD86B"), TextAnchor.MiddleLeft);
            _header.supportRichText = true;
            Ui.BoxButton(_root, 1720, 22, 160, 46, "Back", Close, 18);

            var viewport = Ui.Box(_root, 40, 110, 640, 890);
            Ui.Image(viewport, new Color(0, 0, 0, 0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            _list = Ui.Rect("Recipes", viewport);
            _list.anchorMin = new Vector2(0, 1);
            _list.anchorMax = new Vector2(1, 1);
            _list.pivot = new Vector2(0.5f, 1);
            _list.sizeDelta = Vector2.zero;
            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _list;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40;

            _detail = Ui.Box(_root, 700, 110, 1180, 890);
            Ui.Image(_detail, Ui.Hex("#1A1F2B"));
            _status = Ui.Text(Ui.Box(_root, 40, 1012, 1840, 50), "", 16, new Color(0.8f, 0.83f, 0.9f), TextAnchor.MiddleLeft);
            _status.supportRichText = true;
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _library.Refresh();
            _mine = null; // a built-in recipe first, as before (your own are a click away at the top)
            _deleteArmed = null;
            _recipes = RecipeBook.All(Circuits);
            // Start on the newest unlock you haven't looked at, else the first unlocked one.
            int fresh = _recipes.FindIndex(r => Unlocked(r) && !Profile.Seen.Contains(RecipeBook.SeenKey(r)));
            _selected = fresh >= 0 ? fresh : Math.Max(0, _recipes.FindIndex(Unlocked));
            _scrollToSelected = true;
            _status.text = "Beat a duelist while they play a deck to unlock it here. Build turns a recipe into a deck of yours, from the cards you own.";
            Refresh();
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>Steps aside without <see cref="Closed"/> (a lesson plays; <see cref="Open"/> brings it back).</summary>
        public void Hide() => _root.gameObject.SetActive(false);

        private bool Unlocked(Recipe r) => RecipeBook.IsUnlocked(r, Circuits, Profile);

        private Deck? Load(Recipe r)
        {
            if (!_library.Contains(r.Deck)) return null;
            var deck = _library.Load(r.Deck);
            deck.Name = r.Deck;
            return deck;
        }

        private static string Title(Recipe r, Deck? deck)
        {
            var title = deck?.Note("title") ?? "";
            if (title.Length > 0) return title;
            return string.Join(" ", r.Deck.Split('-').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w.Substring(1)));
        }

        private Banlist List(Recipe r) => _state.ListFor(r.Circuit) ?? Banlist.None;

        /// <summary>A recipe pack's price: by the band you first meet the deck in.</summary>
        private int PackPrice(Recipe r) => r.Circuit >= 0 && r.Circuit < Circuits.All.Count ? RecipeShop.PackPrice(Circuits.All[r.Circuit].Band) : RecipeShop.BasePackPrice;

        /// <summary>Owned copies over wanted copies of a recipe's Main and Extra Deck cards (within its ban list).</summary>
        private (int owned, int wanted) Ownership(Recipe r, Deck deck)
        {
            int owned = 0, wanted = 0;
            foreach (var w in AutoDeck.Wants(deck, List(r), _cards))
            {
                wanted += w.Value;
                owned += Math.Min(w.Value, Profile.Collection.Count(w.Key));
            }
            return (owned, wanted);
        }

        // ---- The list ----------------------------------------------------------------------------------

        private void Refresh()
        {
            var unlocked = _recipes.Count(Unlocked);
            int fresh = _recipes.Count(r => Unlocked(r) && !Profile.Seen.Contains(RecipeBook.SeenKey(r)));
            var mine = _state.MyRecipes.Names;
            if (_mine != null && !mine.Contains(_mine)) _mine = null;
            _header.text = $"{unlocked} of {_recipes.Count} recipes unlocked" + (mine.Count > 0 ? $"   ·   {mine.Count} of your own" : "")
                           + (fresh > 0 ? $"   <color=#7BD88F>{fresh} new</color>" : "");

            for (int i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            float y = DrawMine(mine);
            string band = "";
            for (int i = 0; i < _recipes.Count; i++)
            {
                var r = _recipes[i];
                var circuit = Circuits.All[r.Circuit];
                if (circuit.Band != band)
                {
                    band = circuit.Band;
                    var heading = Ui.Text(Ui.Box(_list, 6, y + 8, 620, 28), band.ToUpperInvariant(), 16, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
                    heading.fontStyle = FontStyle.Bold;
                    y += 40;
                }
                if (i == _selected) _rowTop = y;
                DrawRow(i, r, y);
                y += Row;
            }
            _list.sizeDelta = new Vector2(0, y);
            ShowDetail();
            if (_scrollToSelected)
            {
                _scrollToSelected = false;
                Canvas.ForceUpdateCanvases();
                var max = Mathf.Max(1, y - _scroll.viewport.rect.height);
                _scroll.verticalNormalizedPosition = 1 - Mathf.Clamp01((_rowTop - _scroll.viewport.rect.height / 2) / max);
            }
        }

        private void DrawRow(int index, Recipe r, float y)
        {
            bool open = Unlocked(r);
            bool fresh = open && !Profile.Seen.Contains(RecipeBook.SeenKey(r));
            var row = Ui.Box(_list, 0, y, 630, Row - 6);
            Ui.Image(row, index == _selected ? Ui.Hex("#2E3A55") : open ? Ui.Hex("#1E2535") : Ui.Hex("#161A22"));
            // Their face in a round window (dark until unlocked).
            var face = Ui.Box(row, 8, 6, 64, 64);
            Ui.Image(face, Ui.Hex("#232A3A")).raycastTarget = false;
            face.gameObject.AddComponent<RectMask2D>();
            if (Portraits.PlaceHead(face, r.Owner.Id) && face.GetChild(face.childCount - 1).GetComponent<RawImage>() is RawImage portrait)
                portrait.color = open ? Color.white : new Color(0.18f, 0.18f, 0.22f);
            var deck = open ? Load(r) : null;
            var name = Ui.Text(Ui.Box(row, 84, 8, 380, 30), open ? Title(r, deck) : "???", 19, open ? Color.white : new Color(1, 1, 1, 0.45f), TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(row, 84, 40, 400, 26), open ? $"{r.Owner.Name}{(r.Boss ? "  ·  <color=#FFC940>boss</color>" : "")}" : $"Beat {r.Owner.Name} in {r.Where}",
                14, new Color(0.72f, 0.76f, 0.84f, open ? 1 : 0.6f), TextAnchor.MiddleLeft).supportRichText = true;
            if (open && deck != null)
            {
                var (owned, wanted) = Ownership(r, deck);
                float frac = wanted == 0 ? 0 : (float)owned / wanted;
                var back = Ui.Box(row, 490, 30, 120, 12);
                Ui.Image(back, new Color(0, 0, 0, 0.5f)).raycastTarget = false;
                Ui.Image(Ui.Box(back, 0, 0, 120 * frac, 12), frac >= 1 ? Ui.Hex("#7BD88F") : Ui.Hex("#5FA8E8")).raycastTarget = false;
                Ui.Text(Ui.Box(row, 490, 44, 120, 22), $"own {Mathf.RoundToInt(frac * 100)}%", 13, new Color(0.8f, 0.84f, 0.92f));
            }
            else if (!open)
            {
                var lockIcon = Ui.Box(row, 560, 22, 32, 32).gameObject.AddComponent<RawImage>();
                lockIcon.texture = Icons.For("lock");
                lockIcon.color = new Color(0.6f, 0.63f, 0.7f);
                lockIcon.raycastTarget = false;
            }
            if (fresh)
            {
                var tag = Ui.Box(row, 490, 6, 50, 20);
                Ui.Image(tag, Ui.Hex("#2F8F4A")).raycastTarget = false;
                Ui.Text(tag, "NEW", 12, Color.white).fontStyle = FontStyle.Bold;
            }
            Ui.Clickable(row, () => { _selected = index; _mine = null; Refresh(); }, hover: () => _keys.Point(index), style: HoverStyle.Row);
        }

        // ---- The keyboard ------------------------------------------------------------------------------

        // Up/down (or the d-pad) move the highlight through the recipes, as a click on a row would (ArrowNav); left/right turn
        // the page (a list's height of rows); the list scrolls to it. Enter on an unlocked recipe you don't own all of asks
        // "Buy a recipe pack of X for N DP?" (KeyConfirm: a second Enter buys as the pack button does, Escape cancels).
        // Build, "Buy what's missing" and the rest stay clicks.
        private readonly ArrowNav _keys = new ArrowNav();
        private readonly KeyConfirm _confirm = new KeyConfirm();

        /// <summary>For checks: the keyboard's buy question as shown ("" when none is up).</summary>
        public string DebugConfirm => _confirm.Question;

        private void Update()
        {
            if (!IsOpen) { _confirm.Close(); return; }
            ArrowNav.DropButtonFocus(_root); // a clicked Build or pack button mustn't take Enter
            if (_confirm.Update()) return;
            var k = _keys.Read(_root, _detail);
            if (!k.Any || _recipes.Count == 0) return;
            int to = -1;
            int from = k.Move != Vector2Int.zero ? _keys.From(_selected, _recipes.Count) : _selected; // on from the row the mouse last pointed at
            if (k.Move.y != 0) to = ArrowNav.NextRow(_recipes.Count, from, k.Move.y, _ => true);
            else if (k.Move.x != 0)
            {
                // A page: as many rows as the list shows, to the first or last recipe at the ends.
                int page = Mathf.Max(1, Mathf.FloorToInt(_scroll.viewport.rect.height / Row));
                int next = Mathf.Clamp(from + k.Move.x * page, 0, _recipes.Count - 1);
                if (next != _selected) to = next;
            }
            if (to >= 0)
            {
                _mine = null;
                _selected = to;
                _scrollToSelected = true;
                Refresh();
                return;
            }
            if (k.Enter && _mine == null) AskToBuyPack(); // your own recipes sell nothing
        }

        /// <summary>Enter on a recipe: its recipe pack asked first (KeyConfirm), then bought as the pack button buys it.</summary>
        private void AskToBuyPack()
        {
            var r = _recipes[Mathf.Clamp(_selected, 0, _recipes.Count - 1)];
            if (!Unlocked(r) || _state.Printings == null || Load(r) is not Deck deck) return;
            var (owned, wanted) = Ownership(r, deck);
            if (wanted == 0 || owned >= wanted) return; // the detail offers no pack then
            int price = PackPrice(r);
            if (Profile.Dp < price) { BuyRecipePack(r, deck); return; } // its own line says why
            _confirm.Open(_root, $"Buy a recipe pack of <b>{Title(r, deck)}</b> for {price:N0} DP?", "Buy", () => BuyRecipePack(r, deck));
        }

        /// <summary>For checks: the index of the recipe selected.</summary>
        public int DebugSelectedIndex => _selected;

        /// <summary>For checks: the recipe selected (its deck).</summary>
        public string DebugSelectedDeck => _recipes.Count == 0 ? "" : _recipes[Mathf.Clamp(_selected, 0, _recipes.Count - 1)].Deck;

        // ---- One recipe --------------------------------------------------------------------------------

        private void ShowDetail()
        {
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            if (_mine != null) { ShowMine(_mine); return; }
            if (_recipes.Count == 0) return;
            var r = _recipes[Mathf.Clamp(_selected, 0, _recipes.Count - 1)];
            var circuit = Circuits.All[r.Circuit];
            bool open = Unlocked(r);
            var figure = Ui.Box(_detail, 16, 16, 230, 330);
            if (Portraits.Place(figure, r.Owner.Id) && figure.GetChild(figure.childCount - 1).GetComponent<RawImage>() is RawImage body && !open)
                body.color = new Color(0.12f, 0.12f, 0.16f);

            if (!open)
            {
                Ui.Text(Ui.Box(_detail, 270, 30, 880, 50), "Locked recipe", 30, Color.white, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                var how = Ui.Text(Ui.Box(_detail, 270, 90, 880, 120),
                    $"Beat <b>{r.Owner.DisplayName}</b> in <b>{r.Where}</b> ({circuit.Band}) to unlock the deck they play there.\n\n"
                    + (Circuits.IsUnlocked(r.Circuit, Profile) ? "That circuit is open now." : $"That circuit opens later on the map."),
                    18, new Color(0.82f, 0.85f, 0.92f), TextAnchor.UpperLeft);
                how.supportRichText = true;
                return;
            }

            if (Profile.Seen.Add(RecipeBook.SeenKey(r))) _state.Save();
            var deck = Load(r);
            if (deck == null)
            {
                Ui.Text(Ui.Box(_detail, 270, 30, 880, 50), $"{r.Deck} (deck file missing)", 22, Color.white, TextAnchor.MiddleLeft);
                return;
            }
            var list = List(r);
            var title = Ui.Text(Ui.Box(_detail, 270, 18, 890, 46), Title(r, deck), 32, Color.white, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 18;
            title.resizeTextMaxSize = 32;
            var era = deck.Note("era");
            var by = Ui.Text(Ui.Box(_detail, 270, 64, 890, 26),
                $"{r.Owner.DisplayName}'s deck  ·  {r.Where}" + (era.Length > 0 ? $"  ·  <color=#FFC940>{era}{(deck.Note("tier").Length > 0 ? $", {deck.Note("tier")}" : "")}</color>" : ""),
                15, new Color(0.75f, 0.78f, 0.86f), TextAnchor.MiddleLeft);
            by.supportRichText = true;
            // As a loaner today: what the current circuit's ban list does to it.
            var now = _state.ListFor(_state.CurrentCircuit);
            var changes = now == null || now.Name == list.Name ? new List<string>() : _state.LoanerChanges(r.Deck, _state.CurrentCircuit).Where(c => c.StartsWith("Removed")).ToList();
            var banLine = Ui.Text(Ui.Box(_detail, 270, 90, 890, 24), (list != Banlist.None ? $"Ban list: {list.Name}"
                    : _state.Profile?.EraLimited == true ? "Plays with no ban list (house rules)." : "Lent as built: ban lists only apply in era-limited play.")
                + (now == null || now.Name == list.Name ? "" : changes.Count == 0 ? $"   ·   As a loaner under {now.Name}: unchanged"
                    : $"   ·   As a loaner under {now.Name}: <color=#FF8A8A>{string.Join(", ", changes.Take(2).Select(c => c.Replace("Removed ", "−")))}{(changes.Count > 2 ? $" (+{changes.Count - 2})" : "")}</color>"),
                14, Ui.Hex("#E3A21A"), TextAnchor.MiddleLeft);
            banLine.supportRichText = true;
            banLine.resizeTextForBestFit = true;
            banLine.resizeTextMinSize = 10;
            banLine.resizeTextMaxSize = 14;

            // Its story, from the deck's notes.
            var about = deck.Note("about");
            var why = deck.Note("why");
            var story = about.Length > 0 || why.Length > 0
                ? (about.Length > 0 ? $"<b>How it worked:</b> {about}\n" : "") + (why.Length > 0 ? $"<b>Why it mattered:</b> {why}" : "")
                : $"<i>\"{r.Owner.Intro}\"</i>\n{DeckRules.Breakdown(deck.Main, _cards)}";
            var storyText = Ui.Text(Ui.Box(_detail, 270, 120, 600, 140), story, 15, new Color(0.86f, 0.88f, 0.93f), TextAnchor.UpperLeft);
            storyText.supportRichText = true;
            storyText.resizeTextForBestFit = true;
            storyText.resizeTextMinSize = 11;
            storyText.resizeTextMaxSize = 15;

            // How much of it you own.
            var (owned, wanted) = Ownership(r, deck);
            float frac = wanted == 0 ? 0 : (float)owned / wanted;
            Ui.Text(Ui.Box(_detail, 270, 268, 300, 24), "YOU OWN", 13, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            var bar = Ui.Box(_detail, 270, 294, 600, 18);
            Ui.Image(bar, new Color(0, 0, 0, 0.5f));
            Ui.Image(Ui.Box(bar, 0, 0, 600 * frac, 18), frac >= 1 ? Ui.Hex("#7BD88F") : Ui.Hex("#5FA8E8")).raycastTarget = false;
            Ui.Text(Ui.Box(_detail, 884, 288, 270, 30), $"{owned} of {wanted} cards ({Mathf.RoundToInt(frac * 100)}%)", 16, Color.white, TextAnchor.MiddleLeft);
            // Recipe packs: 9 of its cards (the missing ones first), or every missing card bought outright.
            if (_state.Printings != null && frac < 1)
            {
                var printings = _state.Printings;
                int year = _state.ShopYear;
                int packPrice = PackPrice(r);
                int complete = RecipeShop.CompletePrice(deck, Profile, list, year, printings, _cards, packPrice);
                var packRecipe = r;
                Ui.BoxButton(_detail, 270, 318, 280, 30, $"Recipe pack · {packPrice:N0} DP", () => BuyRecipePack(packRecipe, deck), 15);
                if (complete > 0)
                    Ui.BoxButton(_detail, 560, 318, 310, 30, $"Buy what's missing · {complete:N0} DP", () => CompleteRecipe(packRecipe, deck), 15);
            }
            // A Replay Lesson: the deck played by its owner, opened at its key turn.
            var lessonRecipe = r;
            Ui.BoxButton(_detail, 884, 186, 270, 36, "Customize as loaner", () => CustomizeLoaner(lessonRecipe, deck), 15);
            Ui.BoxButton(_detail, 884, 228, 270, 36, $"▶ Watch {r.Owner.Name.Split(' ')[0]} play it", () => WatchLesson?.Invoke(lessonRecipe), 15);

            // The list: each card once with its count; ones you're short of are dimmed with what you have.
            Ui.Text(Ui.Box(_detail, 16, 354, 600, 24), $"MAIN DECK {deck.Main.Count}   ·   EXTRA DECK {deck.Extra.Count}", 13, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            var wants = AutoDeck.Wants(deck, list, _cards);
            var groups = deck.Main.Select(c => (c, extra: false)).Concat(deck.Extra.Select(c => (c, extra: true)))
                .GroupBy(x => Banlist.BaseCode(x.c, _cards)).Select(g => (code: g.First().c, key: g.Key, count: g.Count(), extra: g.First().extra)).ToList();
            var size = new Vector2(54, 79);
            int perRow = 19;
            for (int i = 0; i < groups.Count && i < perRow * 4; i++)
            {
                var (code, key, count, extra) = groups[i];
                var info = _cards.Get(code);
                float x = 16 + (i % perRow) * 60 + size.x / 2, y = 384 + (i / perRow) * 100 + size.y / 2;
                var card = info == null ? new FieldCard { Code = code } : new FieldCard
                {
                    Code = code, Type = info.Type, Level = info.Level, Attack = info.Attack, Defense = info.Defense, Position = 0x1, IsPublic = true,
                };
                var view = CardView.Create(_detail, new Vector2(x - 590, 445 - y), size, card, info, 0, true, null, offField: true);
                int have = Math.Min(count, Profile.Collection.Count(key));
                if (have < count) view.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
                var label = Ui.Text(Ui.Placed("Count", _detail, new Vector2(x - 590, 445 - y - size.y / 2 - 9), new Vector2(60, 16)),
                    have < count ? $"<color=#FF8A8A>{have}/{count}</color>" : $"x{count}", 12, Color.white);
                label.supportRichText = true;
            }

            // What you're missing, and where to get it.
            var missing = wants.Where(w => Profile.Collection.Count(w.Key) < w.Value).ToList();
            // Five lines fit: four and "…and N more" when there are more than five.
            int listed = missing.Count > 5 ? 4 : missing.Count;
            var where = missing.Take(listed).Select(w =>
                $"<b>{_cards.Get(w.Key)?.Name ?? w.Key.ToString()}</b> x{w.Value - Profile.Collection.Count(w.Key)}: {WhereToGet(w.Key)}");
            var missingText = Ui.Text(Ui.Box(_detail, 16, 790, 820, 92),
                missing.Count == 0 ? "<color=#7BD88F>You own every card in this recipe.</color>"
                    : $"<color=#FF8A8A>Missing {missing.Count} card{(missing.Count == 1 ? "" : "s")}:</color>\n" + string.Join("\n", where) + (missing.Count > listed ? $"\n…and {missing.Count - listed} more" : ""),
                13, new Color(0.84f, 0.87f, 0.93f), TextAnchor.UpperLeft);
            missingText.supportRichText = true;

            var recipe = r;
            Ui.BoxButton(_detail, 850, 792, 310, 42, "Build this deck", () => BuildDeck(recipe, deck), 18).targetGraphic.color = Ui.Hex("#B8432F");
            if (missing.Count > 0) Ui.BoxButton(_detail, 850, 840, 310, 42, "Add missing to wishlist", () => Wish(recipe, deck), 16);
        }

        private string WhereToGet(uint baseCode) => _state.WhereToGet(baseCode);

        /// <summary>
        /// A custom loaner: the recipe (adjusted for the current ban list) saved as a deck you can edit with its own cards
        /// plus yours. It's still a loaner (half DP, no Ban Passes).
        /// </summary>
        private void CustomizeLoaner(Recipe r, Deck deck)
        {
            var name = _state.CustomizeLoaner(r.Deck, Title(r, deck));
            _status.text = $"Saved <b>{name}</b>. "
                + (_state.Profile?.EraLimited == true ? "Swap out what the ban list took using its own cards and yours" : "Change it using its own cards and yours")
                + "; pick it on the versus screen (half DP, like any loaner).";
            _root.gameObject.SetActive(false);
            EditDeck?.Invoke(name);
        }

        /// <summary>A recipe pack: 9 of the recipe's cards, the ones you're missing first.</summary>
        private void BuyRecipePack(Recipe r, Deck deck)
        {
            int packPrice = PackPrice(r);
            if (Profile.Dp < packPrice)
            {
                _status.text = $"<color=#FF8A8A>Not enough DP:</color> a recipe pack for this deck costs {packPrice:N0} DP, you have {Profile.Dp:N0}.";
                return;
            }
            var pulls = RecipeShop.OpenPack(deck, Profile, List(r), _state.ShopYear, _state.Printings!, _cards, _rng, packPrice);
            _state.Save();
            var names = pulls.Select(p => (_cards.Get(p.pull.Code)?.Name ?? "?") + (p.isNew ? " <color=#7BD88F>NEW</color>" : ""));
            _status.text = $"<b>Recipe pack ({Title(r, deck)}):</b> {string.Join(", ", names)}.  {Profile.Dp:N0} DP left.";
            if (_state.CheckSets() is { Length: > 0 } sets) _status.text += $"  <color={UiTheme.Current.GoldSoftHex}>{sets}</color>";
            Sounds.Play("rare", 0.6f);
            Refresh();
        }

        /// <summary>Buys every card the recipe still needs, at a price by rarity.</summary>
        private void CompleteRecipe(Recipe r, Deck deck)
        {
            int price = RecipeShop.CompletePrice(deck, Profile, List(r), _state.ShopYear, _state.Printings!, _cards, PackPrice(r));
            if (Profile.Dp < price)
            {
                _status.text = $"<color=#FF8A8A>Not enough DP:</color> the missing cards cost {price:N0} DP, you have {Profile.Dp:N0}. Recipe packs are the cheaper way.";
                return;
            }
            int added = RecipeShop.Complete(deck, Profile, List(r), _state.ShopYear, _state.Printings!, _cards, PackPrice(r));
            _state.Save();
            _status.text = $"Bought {added} card{(added == 1 ? "" : "s")} for {Title(r, deck)} ({price:N0} DP). Build this deck to play it as your own.";
            if (_state.CheckSets() is { Length: > 0 } sets) _status.text += $"  <color={UiTheme.Current.GoldSoftHex}>{sets}</color>";
            Sounds.Play("level_up", 0.6f);
            Refresh();
        }

        private readonly System.Random _rng = new System.Random();

        /// <summary>For checks: buys a recipe pack for the selected recipe; returns the DP it cost (0 if it couldn't).</summary>
        public int DebugRecipePack()
        {
            var r = _recipes[Mathf.Clamp(_selected, 0, _recipes.Count - 1)];
            var deck = Load(r);
            if (deck == null || _state.Printings == null) return 0;
            int before = Profile.Dp;
            BuyRecipePack(r, deck);
            return before - Profile.Dp;
        }

        /// <summary>For checks: the outright price of the selected recipe's missing cards.</summary>
        public int DebugCompletePrice()
        {
            var r = _recipes[Mathf.Clamp(_selected, 0, _recipes.Count - 1)];
            var deck = Load(r);
            return deck == null || _state.Printings == null ? 0 : RecipeShop.CompletePrice(deck, Profile, List(r), _state.ShopYear, _state.Printings, _cards, PackPrice(r));
        }

        private void Wish(Recipe r, Deck deck)
        {
            Profile.Wishlist.Clear();
            foreach (var w in AutoDeck.Wants(deck, List(r), _cards)) Profile.Wishlist[w.Key] = w.Value;
            Profile.WishlistFor = r.Deck;
            _state.Save();
            _status.text = $"<color=#7BD88F>Wishlist set to {Title(r, deck)}.</color> The Card Shop ranks its packs by these cards, and the Card Trader lists them first.";
            // Bounties claimed before there was a Recipe are paid from it now (card unlocks).
            if (_state.PayOwed() is { Length: > 0 } paid) _status.text += $"  <color={UiTheme.Current.GoldSoftHex}>{paid}</color>";
        }

        private void BuildDeck(Recipe r, Deck recipe)
        {
            var result = AutoDeck.FromRecipe(recipe, Profile.Collection, List(r), _cards);
            var deck = result.Deck;
            // (Named as the library saves it: a title like "Ryzeal / Mitsurugi Ryzeal" loses its slash.)
            var file = DeckLibrary.SafeName(Title(r, recipe));
            var name = $"{file} (recipe)";
            for (int n = 2; _library.Contains(name); n++) name = $"{file} (recipe {n})";
            deck.Name = name;
            deck.Campaign = true;
            deck.Notes["recipe"] = r.Deck; // it plays by its recipe's playbook
            _library.Save(deck);
            Profile.Deck = name;
            _state.Save();
            _status.text = $"Built <b>{name}</b> from your cards ({result.Changes.Count - 1} changes for cards you're missing). It's now your campaign deck.";
            _root.gameObject.SetActive(false);
            EditDeck?.Invoke(name);
        }

        /// <summary>For automated checks: selects a recipe by deck name and returns whether it's unlocked.</summary>
        public bool DebugSelect(string deck)
        {
            _selected = Math.Max(0, _recipes.FindIndex(r => r.Deck == deck));
            _scrollToSelected = true;
            Refresh();
            return Unlocked(_recipes[_selected]);
        }

        /// <summary>For automated checks: presses Build on the selected recipe.</summary>
        public void DebugBuild()
        {
            var r = _recipes[_selected];
            if (Load(r) is Deck deck) BuildDeck(r, deck);
        }

        public int DebugCount(out int unlocked)
        {
            unlocked = _recipes.Count(Unlocked);
            return _recipes.Count;
        }
    }
}
