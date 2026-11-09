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
    /// The Recipe Book's "My recipes" (docs/dev/YDK-IMPORT-2026-11-07.md): .ydk files you imported, above the unlocked recipes.
    /// One shows how much of it you own, its cards (ones over the current circuit's ban list flagged, never removed), what
    /// you're missing and where to get it, and Build, Add missing to wishlist, Rename and Delete. Nothing here sells cards:
    /// no recipe pack, no "Buy what's missing", no loaner, no lesson.
    /// </summary>
    public sealed partial class RecipeBookScreen
    {
        /// <summary>The recipe of yours shown (its name); null: a built-in recipe is.</summary>
        private string? _mine;
        /// <summary>The recipe Delete was clicked once for (a second click deletes it).</summary>
        private string? _deleteArmed;
        private InputField? _renameField;

        /// <summary>The "My recipes" heading, its Import .ydk button and your recipes' rows; returns the height used.</summary>
        private float DrawMine(List<string> mine)
        {
            float y = 0;
            var heading = Ui.Text(Ui.Box(_list, 6, y + 8, 400, 28), "MY RECIPES", 16, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
            heading.fontStyle = FontStyle.Bold;
            Ui.BoxButton(_list, 470, y + 6, 156, 32, "Import .ydk", PickYdk, 15);
            y += 44;
            if (mine.Count == 0)
            {
                Ui.Text(Ui.Box(_list, 6, y, 620, 44), "Import a .ydk deck list (deck sites and EDOPro export them) to keep it here as a plan to build towards.",
                    14, new Color(0.72f, 0.76f, 0.84f), TextAnchor.UpperLeft);
                return y + 52;
            }
            foreach (var name in mine)
            {
                var deck = _state.MyRecipes.Load(name);
                var row = Ui.Box(_list, 0, y, 630, Row - 6);
                Ui.Image(row, name == _mine ? Ui.Hex("#2E3A55") : Ui.Hex("#1E2535"));
                var badge = Ui.Box(row, 8, 6, 64, 64);
                Ui.Image(badge, Ui.Hex("#232A3A")).raycastTarget = false;
                Ui.Text(badge, "YDK", 16, Ui.Hex("#FFC940")).fontStyle = FontStyle.Bold;
                var title = Ui.Text(Ui.Box(row, 84, 8, 390, 30), name, 19, Color.white, TextAnchor.MiddleLeft);
                title.fontStyle = FontStyle.Bold;
                title.resizeTextForBestFit = true;
                title.resizeTextMinSize = 12;
                title.resizeTextMaxSize = 19;
                Ui.Text(Ui.Box(row, 84, 40, 400, 26), deck == null ? "can't be read" : $"Your recipe  ·  {deck.Main.Count} / {deck.Extra.Count} / {deck.Side.Count}",
                    14, new Color(0.72f, 0.76f, 0.84f), TextAnchor.MiddleLeft);
                if (deck != null)
                {
                    var (owned, wanted) = MineOwnership(deck);
                    float frac = wanted == 0 ? 0 : (float)owned / wanted;
                    var back = Ui.Box(row, 490, 30, 120, 12);
                    Ui.Image(back, new Color(0, 0, 0, 0.5f)).raycastTarget = false;
                    Ui.Image(Ui.Box(back, 0, 0, 120 * frac, 12), frac >= 1 ? Ui.Hex("#7BD88F") : Ui.Hex("#5FA8E8")).raycastTarget = false;
                    Ui.Text(Ui.Box(row, 490, 44, 120, 22), $"own {Mathf.RoundToInt(frac * 100)}%", 13, new Color(0.8f, 0.84f, 0.92f));
                }
                var pick = name;
                Ui.Clickable(row, () => { _mine = pick; _deleteArmed = null; Refresh(); }, style: HoverStyle.Row);
                y += Row;
            }
            return y + 6;
        }

        /// <summary>Owned copies over wanted copies of a recipe of yours (Main and Extra, within the current list).</summary>
        private (int owned, int wanted) MineOwnership(Deck deck)
        {
            int owned = 0, wanted = 0;
            foreach (var w in AutoDeck.Wants(deck, _state.MyRecipeList, _cards))
            {
                wanted += w.Value;
                owned += Math.Min(w.Value, Profile.Collection.Count(w.Key));
            }
            return (owned, wanted);
        }

        private void ShowMine(string name)
        {
            var deck = _state.MyRecipes.Load(name);
            if (deck == null)
            {
                Ui.Text(Ui.Box(_detail, 16, 30, 1100, 50), $"{name} (its file can't be read)", 22, Color.white, TextAnchor.MiddleLeft);
                return;
            }
            var list = _state.MyRecipeList;
            var title = Ui.Text(Ui.Box(_detail, 16, 18, 840, 46), name, 32, Color.white, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 18;
            title.resizeTextMaxSize = 32;
            Ui.Text(Ui.Box(_detail, 16, 64, 840, 26), "Your recipe, imported from a .ydk file: a plan to build towards. Build fills it from your cards; nothing here sells it.",
                15, new Color(0.75f, 0.78f, 0.86f), TextAnchor.MiddleLeft);

            // The ban list it's judged by, and the cards over it (flagged, never removed).
            var over = _state.OverList(deck, list);
            string Limit(int allowed) => allowed == 0 ? (list.Whitelist ? "not legal" : "Forbidden") : allowed == 1 ? "Limited" : allowed == 2 ? "Semi-Limited" : "3 at most";
            var flagged = string.Join(", ", over.Take(2).Select(o => $"{_cards.Get(o.code)?.Name ?? o.code.ToString()} x{o.copies} ({Limit(o.allowed)})"))
                          + (over.Count > 2 ? $" and {over.Count - 2} more (tagged below)" : "");
            var banLine = Ui.Text(Ui.Box(_detail, 16, 90, 1150, 24),
                (list != Banlist.None ? $"Ban list: {list.Name} (the current circuit's)" : "No ban list applies in open play")
                + (over.Count == 0 ? "   ·   nothing over it" : $"   ·   <color=#FF8A8A>Over it: {flagged}</color>"),
                14, Ui.Hex("#E3A21A"), TextAnchor.MiddleLeft);
            banLine.supportRichText = true;
            banLine.resizeTextForBestFit = true;
            banLine.resizeTextMinSize = 10;
            banLine.resizeTextMaxSize = 14;
            // Its sizes, and what the deck rules say (Build and the editor sort it out).
            var sizeProblems = DeckRules.Validate(deck, Banlist.None, _cards).Where(p => p.Code == 0).Select(p => p.Message).ToList();
            var sizes = Ui.Text(Ui.Box(_detail, 16, 116, 1150, 24),
                $"Main {deck.Main.Count}  ·  Extra {deck.Extra.Count}  ·  Side {deck.Side.Count}"
                + (sizeProblems.Count > 0 ? $"   ·   <color=#FF8A8A>{string.Join(" ", sizeProblems)}</color>" : ""),
                14, new Color(0.86f, 0.88f, 0.93f), TextAnchor.MiddleLeft);
            sizes.supportRichText = true;

            // How much of it you own.
            var (owned, wanted) = MineOwnership(deck);
            float frac = wanted == 0 ? 0 : (float)owned / wanted;
            Ui.Text(Ui.Box(_detail, 16, 148, 300, 24), "YOU OWN", 13, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            var bar = Ui.Box(_detail, 16, 174, 600, 18);
            Ui.Image(bar, new Color(0, 0, 0, 0.5f));
            Ui.Image(Ui.Box(bar, 0, 0, 600 * frac, 18), frac >= 1 ? Ui.Hex("#7BD88F") : Ui.Hex("#5FA8E8")).raycastTarget = false;
            Ui.Text(Ui.Box(_detail, 630, 168, 260, 30), $"{owned} of {wanted} cards ({Mathf.RoundToInt(frac * 100)}%)", 16, Color.white, TextAnchor.MiddleLeft);

            // Rename and Delete.
            _renameField = Ui.Input(Ui.Box(_detail, 16, 214, 420, 36), "new name", 16);
            _renameField.SetTextWithoutNotify(name);
            Ui.BoxButton(_detail, 446, 214, 120, 36, "Rename", () => Rename(name, _renameField!.text), 15);
            Ui.BoxButton(_detail, 576, 214, 120, 36, _deleteArmed == name ? "Delete?" : "Delete", () => Delete(name), 15)
                .targetGraphic.color = _deleteArmed == name ? Ui.Hex("#B8432F") : UiTheme.Current.Button;

            // The list: each card once with its count; short ones dimmed with what you have; ones over the list tagged.
            Ui.Text(Ui.Box(_detail, 16, 262, 900, 24), $"MAIN DECK {deck.Main.Count}   ·   EXTRA DECK {deck.Extra.Count}"
                + (deck.Side.Count > 0 ? $"   ·   SIDE DECK {deck.Side.Count} (not built)" : ""), 13, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
            var overCodes = over.ToDictionary(o => o.code, o => o.allowed);
            var groups = deck.Main.Select(c => (c, extra: false)).Concat(deck.Extra.Select(c => (c, extra: true)))
                .GroupBy(x => Banlist.BaseCode(x.c, _cards)).Select(g => (code: g.First().c, key: g.Key, count: g.Count())).ToList();
            var size = new Vector2(54, 79);
            const int perRow = 19, rows = 5;
            for (int i = 0; i < groups.Count && i < perRow * rows; i++)
            {
                var (code, key, count) = groups[i];
                var info = _cards.Get(code);
                float x = 16 + (i % perRow) * 60 + size.x / 2, y = 304 + (i / perRow) * 98 + size.y / 2;
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
                if (overCodes.TryGetValue(key, out var allowed))
                {
                    var tag = Ui.Placed("Over", _detail, new Vector2(x - 590, 445 - y + size.y / 2 - 8), new Vector2(54, 16));
                    Ui.Image(tag, Ui.Hex("#B8432F")).raycastTarget = false;
                    Ui.Text(tag, allowed == 0 ? "BANNED" : $"LIMIT {allowed}", 10, Color.white).fontStyle = FontStyle.Bold;
                }
            }
            if (groups.Count > perRow * rows)
                Ui.Text(Ui.Box(_detail, 16, 790 - 22, 600, 20), $"…and {groups.Count - perRow * rows} more cards", 12, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleLeft);

            // What you're missing, and where to get it.
            var missing = AutoDeck.Wants(deck, list, _cards).Where(w => Profile.Collection.Count(w.Key) < w.Value).ToList();
            int listed = missing.Count > 5 ? 4 : missing.Count;
            var where = missing.Take(listed).Select(w =>
                $"<b>{_cards.Get(w.Key)?.Name ?? w.Key.ToString()}</b> x{w.Value - Profile.Collection.Count(w.Key)}: {WhereToGet(w.Key)}");
            var missingText = Ui.Text(Ui.Box(_detail, 16, 790, 820, 92),
                missing.Count == 0 ? "<color=#7BD88F>You own every card in this recipe.</color>"
                    : $"<color=#FF8A8A>Missing {missing.Count} card{(missing.Count == 1 ? "" : "s")}:</color>\n" + string.Join("\n", where) + (missing.Count > listed ? $"\n…and {missing.Count - listed} more" : ""),
                13, new Color(0.84f, 0.87f, 0.93f), TextAnchor.UpperLeft);
            missingText.supportRichText = true;

            Ui.BoxButton(_detail, 850, 792, 310, 42, "Build this deck", () => BuildMine(name), 18).targetGraphic.color = Ui.Hex("#B8432F");
            if (missing.Count > 0) Ui.BoxButton(_detail, 850, 840, 310, 42, "Add missing to wishlist", () => WishMine(name), 16);
        }

        /// <summary>Import .ydk: macOS's open panel, then the file kept as one of your recipes.</summary>
        private void PickYdk()
        {
            var path = YdkFilePicker.Pick(out var refused);
            if (path == null)
            {
                if (refused != null) _status.text = $"Couldn't open the file dialog: {refused}.";
                return;
            }
            ImportYdk(path);
        }

        /// <summary>A .ydk file kept as one of your recipes (the button's, or a check's path); shown when it worked. Its name, or null.</summary>
        public string? ImportYdk(string path)
        {
            var name = _state.ImportRecipe(path, out var status);
            _status.text = name == null ? $"<color=#FF8A8A>{status}</color>" : status;
            if (name != null)
            {
                _mine = name;
                _deleteArmed = null;
                _scroll.verticalNormalizedPosition = 1;
            }
            Refresh();
            return name;
        }

        private void Rename(string name, string to)
        {
            if (_state.RenameRecipe(name, to, out var newName, out var status)) _mine = newName;
            _status.text = status;
            Refresh();
        }

        private void Delete(string name)
        {
            if (_deleteArmed != name)
            {
                _deleteArmed = name;
                _status.text = $"Delete your recipe <b>{name}</b>? Click Delete again to confirm (decks you built from it stay).";
                Refresh();
                return;
            }
            _deleteArmed = null;
            _status.text = _state.DeleteRecipe(name) ? $"Deleted your recipe {name}." : $"{name} was already gone.";
            _mine = null;
            Refresh();
        }

        private void WishMine(string name)
        {
            if (_state.MyRecipes.Load(name) is not Deck deck) return;
            _state.WishFor(deck);
            _status.text = $"<color=#7BD88F>Wishlist set to {name}.</color> The Card Shop ranks its packs by these cards, and the Card Trader lists them first."
                           + " (Gus's bounties pay only from unlocked recipes.)";
            if (_state.PayOwed() is { Length: > 0 } paid) _status.text += $"  <color={UiTheme.Current.GoldSoftHex}>{paid}</color>";
        }

        /// <summary>Build: a campaign deck from your collection, as a built-in recipe builds (the gaps filled from your spare cards).</summary>
        private void BuildMine(string name)
        {
            if (_state.MyRecipes.Load(name) is not Deck recipe) return;
            var result = AutoDeck.FromRecipe(recipe, Profile.Collection, _state.MyRecipeList, _cards);
            var deck = result.Deck;
            var file = DeckLibrary.SafeName(name);
            var deckName = $"{file} (recipe)";
            for (int n = 2; _library.Contains(deckName); n++) deckName = $"{file} (recipe {n})";
            deck.Name = deckName;
            deck.Campaign = true;
            _library.Save(deck);
            Profile.Deck = deckName;
            _state.Save();
            var cuts = result.Changes.Where(c => c.StartsWith("Cut ")).ToList();
            _status.text = $"Built <b>{deckName}</b> from your cards ({result.Changes.Count - 1} changes for cards you're missing). It's now your campaign deck."
                           + (cuts.Count > 0 ? " " + string.Join(". ", cuts) + "." : "");
            _root.gameObject.SetActive(false);
            EditDeck?.Invoke(deckName);
        }

        // ---- For checks ----

        /// <summary>For checks: your recipes' names as the book lists them.</summary>
        public List<string> DebugMine => _state.MyRecipes.Names;

        /// <summary>For checks: shows one of your recipes; false when it isn't there.</summary>
        public bool DebugSelectMine(string name)
        {
            if (!_state.MyRecipes.Names.Contains(name)) return false;
            _mine = name;
            _deleteArmed = null;
            Refresh();
            return true;
        }

        /// <summary>
        /// For checks: the recipe of yours shown: owned and wanted copies, missing cards, cards flagged over the list, and
        /// every text and every button's label on the detail panel (to find a pack or buy button that mustn't be there).
        /// </summary>
        public (int owned, int wanted, int missing, int flagged, List<string> texts, List<string> buttons) DebugMineDetail()
        {
            if (_mine == null || _state.MyRecipes.Load(_mine) is not Deck deck) return (0, 0, 0, 0, new List<string>(), new List<string>());
            var (owned, wanted) = MineOwnership(deck);
            int missing = AutoDeck.Wants(deck, _state.MyRecipeList, _cards).Count(w => Profile.Collection.Count(w.Key) < w.Value);
            int flagged = _state.OverList(deck, _state.MyRecipeList).Count;
            var texts = _detail.GetComponentsInChildren<Text>(true).Select(t => t.text).ToList();
            var buttons = _detail.GetComponentsInChildren<Button>(true).Select(b => b.GetComponentInChildren<Text>(true)?.text ?? "").ToList();
            return (owned, wanted, missing, flagged, texts, buttons);
        }

        /// <summary>For checks: presses Add missing to wishlist on the recipe of yours shown.</summary>
        public void DebugWishMine() { if (_mine != null) WishMine(_mine); }

        /// <summary>For checks: presses Build on the recipe of yours shown.</summary>
        public void DebugBuildMine() { if (_mine != null) BuildMine(_mine); }

        /// <summary>For checks: types a name and presses Rename.</summary>
        public void DebugRenameMine(string to) { if (_mine != null) Rename(_mine, to); }

        /// <summary>For checks: presses Delete twice (asked, then confirmed); whether the first press only asked.</summary>
        public bool DebugDeleteMine()
        {
            if (_mine == null) return false;
            var name = _mine;
            Delete(name);
            bool asked = _state.MyRecipes.Names.Contains(name) && _deleteArmed == name;
            Delete(name);
            return asked;
        }

        /// <summary>For checks: the status line.</summary>
        public string DebugStatus => _status.text;
    }
}
