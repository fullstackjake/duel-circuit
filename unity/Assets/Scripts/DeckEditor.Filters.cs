using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The search panel's More filters (docs/dev/CARD-SEARCH-SPEC-2026-11-07.md section 7): a panel over the results grid with
    /// the Archetype box, the Limit and Look pickers, Match all / any, and the 30 genres ("what a card does") in six groups.
    /// The results update live behind it (the count above it shows them); everything it sets shows in the active filter
    /// row, removable there. Escape, Close or the controller's B shut it; the controller's View button opens it.
    /// Limit and Look run after <see cref="CardSearch.Find(string, CardSearch.SearchMode, string, int, bool)"/>, as Owned only does: the
    /// ban list is the editor's, and looks are your copies'.
    /// </summary>
    public sealed partial class DeckEditor
    {
        // Limit (section 5): Banlist.Allowed 0 / 1 / 2 / 3, the list's 0-2, or legal here (allowed and already released).
        private static readonly string[] LimitLabels = { "Any", "Forbidden", "Limited", "Semi-Limited", "Unlimited", "On the list (0-2)", "Legal here" };
        private const int LimitAny = 0, LimitOnList = 5, LimitLegal = 6;
        // Look (section 6.3): your owned copies (not those at the Grading Desk); free play offers Any and Alternate art only.
        private static readonly string[] LookLabels = { "Any", "Foil", "Slabbed", "Gem Mint", "Worn", "Alternate art" };
        private const int LookAny = 0, LookFoil = 1, LookSlabbed = 2, LookGemMint = 3, LookWorn = 4, LookAltArt = 5;

        /// <summary>The genres picked (their does: tokens), in the order they were picked.</summary>
        private readonly List<string> _genres = new List<string>();
        /// <summary>Match any: the picked genres are one OR group; else every one must match.</summary>
        private bool _matchAny;
        /// <summary>The archetypes picked (members of any of them).</summary>
        private readonly List<(ushort code, string name)> _archetypePicks = new List<(ushort, string)>();
        private int _limit, _look;
        /// <summary>Which cards have another artwork in the database (any mode), worked out as results ask.</summary>
        private readonly Dictionary<uint, bool> _altArt = new Dictionary<uint, bool>();

        private RectTransform _more = null!, _archetypeRow = null!, _suggestions = null!;
        private Text _moreLabel = null!, _limitLabel = null!, _lookLabel = null!, _limitCaption = null!;
        private Image _moreFill = null!, _allImage = null!, _anyImage = null!;
        private InputField _archetypeField = null!;
        private readonly Dictionary<string, Image> _genreChips = new Dictionary<string, Image>();

        private bool MoreOpen => _more != null && _more.gameObject.activeSelf;

        /// <summary>How many of the panel's filters are on (the More button's "More · 3").</summary>
        private int MoreCount => _genres.Count + _archetypePicks.Count + (_limit != LimitAny ? 1 : 0) + (_look != LookAny ? 1 : 0);

        /// <summary>A Limit filter, picked or typed (limit:0-3, legal), reads the picked list: changing the list re-runs the search.</summary>
        private bool BanFilterOn
        {
            get
            {
                CardSearch.TakeBanFilters(_query, out CardSearch.BanTerms typed);
                return _limit != LimitAny || typed.Any;
            }
        }

        private void BuildMoreButton(float x, float y, float w, float h)
        {
            var T = UiTheme.Current;
            var rt = Box(_root, x, y, w, h);
            _moreFill = Ui.Image(rt, T.Button);
            _moreLabel = Ui.Text(rt, "More", 13, T.Text);
            _moreLabel.resizeTextForBestFit = true;
            _moreLabel.resizeTextMinSize = 10;
            _moreLabel.resizeTextMaxSize = 13;
            Ui.Clickable(rt, ToggleMore, style: HoverStyle.Button);
            var tip = rt.gameObject.AddComponent<TipOnHover>();
            tip.Title = "More filters";
            tip.Body = "What a card does (30 genres), its archetype, its ban list status, and in the campaign your copies' looks.";
        }

        private void BuildMorePanel()
        {
            var T = UiTheme.Current;
            // Over the results grid, the same size; it takes the pointer so the cards under it don't show on hover.
            _more = Box(_root, 1410, 184, 500, 750);
            Ui.Image(_more, T.PanelDeep).raycastTarget = true;
            _more.gameObject.AddComponent<Outline>().effectColor = T.Focus;
            Label(16, 10, 230, 30, "MORE FILTERS", 15, parent: _more).color = T.Caption;
            Button(262, 10, 110, 30, "Clear these", () => { ClearMoreFilters(); RunSearch(); }, _more);
            Button(380, 10, 104, 30, "Close", CloseMore, _more);

            // Archetype: two letters or more for up to 8 suggestions ("Burning Abyss · 24"); a pick adds @name.
            Label(16, 50, 300, 18, "ARCHETYPE", 12, parent: _more).color = T.Caption;
            _archetypeField = Ui.Input(Box(_more, 16, 70, 300, 32), "two letters or more...", 15, ShowSuggestions, _ =>
            {
                // Enter picks the first suggestion (leaving the box any other way doesn't).
#if ENABLE_INPUT_SYSTEM
                var keys = UnityEngine.InputSystem.Keyboard.current;
                bool enter = keys != null && (keys.enterKey.wasPressedThisFrame || keys.numpadEnterKey.wasPressedThisFrame);
#else
                bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
                var first = _archetypeField.text.Trim().Length >= 2 ? _search.ArchetypeSuggestions(_archetypeField.text, 1) : null;
                if (enter && first is { Count: > 0 }) PickArchetype(first[0].code, first[0].name);
            });
            var hint = Label(326, 68, 158, 36, "Its members only: Related finds cards that name it.", 12, TextAnchor.MiddleLeft, _more);
            hint.color = T.TextSoft;
            _archetypeRow = Box(_more, 16, 108, 468, 24);

            // Limit, under the picked ban list; Look, over your copies.
            _limitCaption = Label(16, 142, 226, 18, "LIMIT", 12, parent: _more);
            _limitCaption.color = T.Caption;
            _limitCaption.resizeTextForBestFit = true; // (the list's name rides along: a long one shrinks)
            _limitCaption.resizeTextMinSize = 9;
            _limitCaption.resizeTextMaxSize = 12;
            Label(258, 142, 226, 18, "LOOK", 12, parent: _more).color = T.Caption;
            _limitLabel = MorePicker(16, 162, 226, d => { _limit = (_limit + d + LimitLabels.Length) % LimitLabels.Length; RunSearch(); },
                "Limit", "The picked ban list's limit: Forbidden (Not legal under a whitelist list such as Goat), Limited, Semi-Limited, Unlimited, "
                         + "on the list at all, or Legal here (allowed and already out in the list's format). Typed: limit:0 to limit:3, legal.");
            _lookLabel = MorePicker(258, 162, 226, CycleLook,
                "Look", "Campaign: your copies (not those at the Grading Desk) that are foil (Super Rare or rarer), slabbed, Gem Mint (a 10) "
                        + "or worn (4 or under). Any mode: cards with another artwork in the database.");

            // Match all / Match any (Master Duel's AND / OR) over the genres.
            Label(16, 206, 240, 26, "WHAT IT DOES", 12, parent: _more).color = T.Caption;
            _allImage = MoreSegment(284, 206, "Match all", false);
            _anyImage = MoreSegment(386, 206, "Match any", true);

            // The genres, grouped, four to a row; each explains itself on hover.
            float y = 240;
            foreach (var group in CardTags.Groups)
            {
                Label(16, y, 468, 16, group.ToUpperInvariant(), 12, parent: _more).color = T.Caption;
                y += 18;
                var genres = CardTags.GenresIn(group);
                for (int i = 0; i < genres.Count; i++)
                {
                    var genre = genres[i];
                    var rt = Box(_more, 16 + (i % 4) * 118, y + (i / 4) * 30, 114, 26);
                    _genreChips[genre.Token] = Ui.Image(rt, T.Inset);
                    var label = Ui.Text(rt, genre.Label, 13, T.Text);
                    label.resizeTextForBestFit = true;
                    label.resizeTextMinSize = 10;
                    label.resizeTextMaxSize = 13;
                    label.raycastTarget = false;
                    Ui.Clickable(rt, () => ToggleGenre(genre.Token), style: HoverStyle.Button);
                    var tip = rt.gameObject.AddComponent<TipOnHover>();
                    tip.Title = genre.Label;
                    tip.Body = genre.Meaning + $"\n\nTyped: does:{genre.Token}";
                }
                y += (genres.Count + 3) / 4 * 30 + 6;
            }
            var typed = Label(16, 700, 468, 40, "Typed in the box: does:banish, @burningabyss, name:dark or $dark, text:dragon, pend:destroy, "
                                                 + "a or b, limit:1, legal.", 12, TextAnchor.UpperLeft, _more);
            typed.color = T.TextSoft;

            // The suggestions drop over everything under the box.
            _suggestions = Box(_more, 16, 102, 300, 0);
            Ui.Image(_suggestions, T.Panel).raycastTarget = true;
            _suggestions.gameObject.AddComponent<Outline>().effectColor = T.Focus;
            _suggestions.gameObject.SetActive(false);
            _more.gameObject.SetActive(false);
        }

        /// <summary>A picker in the panel (click: the next option, right-click: the previous); its label.</summary>
        private Text MorePicker(float x, float y, float w, Action<int> step, string title, string help)
        {
            var T = UiTheme.Current;
            var rt = Box(_more, x, y, w, 30);
            Ui.Image(rt, T.Inset);
            var label = Ui.Text(rt, "", 14, T.Text);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 14;
            Ui.Clickable(rt, () => step(+1), () => step(-1));
            var tip = rt.gameObject.AddComponent<TipOnHover>();
            tip.Title = title;
            tip.Body = help + "\n\nClick for the next option, right-click for the previous.";
            return label;
        }

        private Image MoreSegment(float x, float y, string label, bool any)
        {
            var T = UiTheme.Current;
            var rt = Box(_more, x, y, 98, 26);
            var image = Ui.Image(rt, T.ButtonOff);
            Ui.Text(rt, label, 13, T.Text).raycastTarget = false;
            Ui.Clickable(rt, () => { _matchAny = any; RunSearch(); }, style: HoverStyle.Button);
            var tip = rt.gameObject.AddComponent<TipOnHover>();
            tip.Title = label;
            tip.Body = any ? "Cards that do at least one of the genres picked." : "Cards that do every genre picked.";
            return image;
        }

        private void ToggleMore()
        {
            if (MoreOpen) CloseMore();
            else OpenMore();
        }

        private void OpenMore()
        {
            CloseReading();
            _more.gameObject.SetActive(true);
            _more.SetAsLastSibling();
            ShowMorePanel();
        }

        private void CloseMore()
        {
            if (_more == null) return;
            HideSuggestions();
            _more.gameObject.SetActive(false);
            TermTooltip.Hide();
        }

        /// <summary>Clear these: only the panel's filters (the box, chips, pickers and scope stay).</summary>
        private void ClearMoreFilters()
        {
            _genres.Clear();
            _archetypePicks.Clear();
            _matchAny = false;
            _limit = LimitAny;
            _look = LookAny;
            if (_archetypeField != null) _archetypeField.SetTextWithoutNotify("");
            HideSuggestions();
        }

        private void ToggleGenre(string token)
        {
            if (!_genres.Remove(token)) _genres.Add(token);
            RunSearch();
        }

        private void CycleLook(int delta)
        {
            // Free play has no copies: Any and Alternate art only.
            var looks = OwnedCards != null ? new[] { LookAny, LookFoil, LookSlabbed, LookGemMint, LookWorn, LookAltArt } : new[] { LookAny, LookAltArt };
            int at = Math.Max(0, Array.IndexOf(looks, _look));
            _look = looks[(at + delta + looks.Length) % looks.Length];
            RunSearch();
        }

        // ---- Archetype suggestions ---------------------------------------------------------------------------

        private void ShowSuggestions(string typed)
        {
            var T = UiTheme.Current;
            ClearChildren(_suggestions);
            // Two letters at least (one letter matches half the list).
            if ((typed ?? "").Trim().Length < 2) { HideSuggestions(); return; }
            var found = _search.ArchetypeSuggestions(typed!, 8);
            float h = Math.Max(1, found.Count) * 28;
            _suggestions.sizeDelta = new Vector2(300, h + 2);
            if (found.Count == 0)
                Label(10, 1, 280, 28, "No archetype with 2+ cards is called that.", 13, parent: _suggestions).color = T.TextSoft;
            for (int i = 0; i < found.Count; i++)
            {
                var (code, name, count) = found[i];
                var row = Box(_suggestions, 1, 1 + i * 28, 298, 28);
                Ui.Image(row, i % 2 == 0 ? T.RowA : T.RowB);
                var label = Ui.Text(Box(row, 10, 0, 280, 28), $"{name}  <color={T.TextDimHex}>· {count}</color>", 14, T.Text, TextAnchor.MiddleLeft);
                label.supportRichText = true;
                label.raycastTarget = false;
                Ui.Clickable(row, () => PickArchetype(code, name), style: HoverStyle.Row);
            }
            _suggestions.gameObject.SetActive(true);
            _suggestions.SetAsLastSibling();
        }

        private void HideSuggestions()
        {
            if (_suggestions == null) return;
            ClearChildren(_suggestions);
            _suggestions.gameObject.SetActive(false);
        }

        private void PickArchetype(ushort code, string name)
        {
            if (!_archetypePicks.Any(a => a.code == code)) _archetypePicks.Add((code, name));
            _archetypeField.SetTextWithoutNotify("");
            HideSuggestions();
            RunSearch();
        }

        // ---- What the panel adds to a search -------------------------------------------------------------------

        /// <summary>
        /// The panel's terms for <see cref="CardSearch.Find(string, CardSearch.SearchMode, string, int, bool)"/>: the genres (all of them, or one
        /// <c>does:a or does:b</c> group with Match any) and the archetypes (members of any one picked).
        /// </summary>
        private IEnumerable<string> MoreTokens()
        {
            if (_genres.Count > 0)
                yield return string.Join(_matchAny ? " or " : " ", _genres.Select(t => "does:" + t));
            if (_archetypePicks.Count > 0)
                yield return string.Join(" or ", _archetypePicks.Select(a => CardSearch.ArchetypeTerm(a.name)));
        }

        /// <summary>The panel's filters as the active row's chips (an OR shows as "or Draw").</summary>
        private IEnumerable<(string label, Action remove)> MoreChips()
        {
            for (int i = 0; i < _genres.Count; i++)
            {
                var token = _genres[i];
                var label = CardTags.GenreOf(token)?.Label ?? token;
                yield return ((i > 0 && _matchAny ? "or " : "") + label, () => _genres.Remove(token));
            }
            for (int i = 0; i < _archetypePicks.Count; i++)
            {
                var pick = _archetypePicks[i];
                yield return ((i > 0 ? "or " : "") + pick.name, () => _archetypePicks.RemoveAll(a => a.code == pick.code));
            }
            if (_limit != LimitAny) yield return (LimitLabel(_limit), () => _limit = LimitAny);
            if (_look != LookAny) yield return (LookLabels[_look], () => _look = LookAny);
        }

        /// <summary>
        /// "Forbidden" reads "Not legal" under a whitelist list (Goat), as the grid's under-line does; "On the list" reads
        /// 1-2 there (every card off a whitelist is 0).
        /// </summary>
        private string LimitLabel(int limit) =>
            limit == 1 && _banlists[_banIndex].Whitelist ? "Not legal"
            : limit == LimitOnList && _banlists[_banIndex].Whitelist ? "On the list (1-2)"
            : LimitLabels[limit];

        /// <summary>Limit (picked and typed, -limit:N and -legal too) and Look, on what the search found.</summary>
        private List<CardInfo> PostFilters(List<CardInfo> found, CardSearch.BanTerms typed)
        {
            var list = _banlists[_banIndex];
            int Allowed(CardInfo c) => list.Allowed(c.Code, _cards);
            // Legal here: allowed, and already out in the list's format when the era is limited (the "not released yet" test).
            bool eraChecked = OwnedCards == null || (EraLimited?.Invoke() ?? true) || Format != null;
            var formatEnd = eraChecked ? PoolEnd(list) : "";
            bool Legal(CardInfo c) => Allowed(c) > 0 && DeckRules.Released(Banlist.BaseCode(c.Code, _cards), formatEnd, CardFoil.Printings, _cards);
            bool LimitMatches(CardInfo c, int limit) => limit switch
            {
                // A whitelist (Goat) holds 0 for every card it doesn't list: there the list's cards are its 1s and 2s.
                LimitOnList => list.Whitelist ? Allowed(c) is 1 or 2 : Allowed(c) < 3,
                LimitLegal => Legal(c),
                _ => Allowed(c) == limit - 1,
            };
            if (_limit != LimitAny) found = found.Where(c => LimitMatches(c, _limit)).ToList();
            // (A typed limit: outside 0-3 names no limit: it filters nothing.)
            if (typed.Limit is int n && n >= 0 && n <= 3) found = found.Where(c => Allowed(c) == n).ToList();
            if (typed.Legal) found = found.Where(Legal).ToList();
            var notLimits = typed.NotLimits.Where(l => l >= 0 && l <= 3).ToList();
            if (notLimits.Count > 0) found = found.Where(c => !notLimits.Contains(Allowed(c))).ToList();
            if (typed.NotLegal) found = found.Where(c => !Legal(c)).ToList();
            if (_look == LookAltArt) found = found.Where(HasAltArt).ToList();
            else if (_look != LookAny && OwnedCards != null)
            {
                bool Looks(CardCopy copy) => _look switch
                {
                    LookFoil => copy.Rarity >= Rarity.SuperRare, // the booster's foil slot
                    LookSlabbed => copy.IsSlab,
                    LookGemMint => copy.Shown == 10,
                    _ => copy.Shown <= 4, // worn
                };
                found = found.Where(c => OwnedCards.CopiesOf(Banlist.BaseCode(c.Code, _cards)).Any(copy => !copy.AtDesk && Looks(copy))).ToList();
            }
            return found;
        }

        private bool HasAltArt(CardInfo c)
        {
            var key = Banlist.BaseCode(c.Code, _cards);
            if (!_altArt.TryGetValue(key, out var has)) _altArt[key] = has = _versions.Arts(key).Count > 1;
            return has;
        }

        /// <summary>Removes a row's chips now: hidden at once (Destroy waits for the frame's end, and a rebuild in the same frame mustn't count them).</summary>
        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        /// <summary>The More button's count and, while it's open, the panel's chips, pickers and picked archetypes.</summary>
        private void ShowMoreButton()
        {
            var T = UiTheme.Current;
            int on = MoreCount;
            _moreLabel.text = on > 0 ? $"More · {on}" : "More";
            _moreFill.color = on > 0 ? T.TabOn : T.Button;
            if (MoreOpen) ShowMorePanel();
        }

        private void ShowMorePanel()
        {
            var T = UiTheme.Current;
            foreach (var (token, image) in _genreChips) image.color = _genres.Contains(token) ? T.Chip : T.Inset;
            _allImage.color = _matchAny ? T.ButtonOff : T.TabOn;
            _anyImage.color = _matchAny ? T.TabOn : T.ButtonOff;
            _limitLabel.text = LimitLabel(_limit);
            _lookLabel.text = LookLabels[_look];
            _limitCaption.text = $"LIMIT  <color={T.TextDimHex}>({_banlists[_banIndex].Name})</color>";
            _limitCaption.supportRichText = true;
            ClearChildren(_archetypeRow);
            if (_archetypePicks.Count == 0)
                Label(0, 0, 468, 24, "None picked.", 12, parent: _archetypeRow).color = T.TextSoft;
            float x = 0;
            foreach (var (code, name) in _archetypePicks)
            {
                float w = Mathf.Min(200, name.Length * 7.5f + 28);
                if (x + w > 468) break;
                var rt = Box(_archetypeRow, x, 0, w - 4, 24);
                Ui.Image(rt, T.Chip);
                Ui.Text(rt, $"{name} ×", 12, T.Text).raycastTarget = false;
                Ui.Clickable(rt, () => { _archetypePicks.RemoveAll(a => a.code == code); RunSearch(); });
                x += w;
            }
        }

        // ---- From the card detail: more cards like this one ----------------------------------------------------

        /// <summary>
        /// A genre chip on the card detail was clicked: the search becomes that genre (the box, chips, pickers, Related and the
        /// panel's other filters clear; Owned only and the sort stay).
        /// </summary>
        private void SearchGenre(CardTags.Genre genre)
        {
            ResetSearchForChip();
            _genres.Add(genre.Token);
            RunSearch();
            SetStatus($"{_results.Count}{(_results.Count >= 2000 ? "+" : "")} cards that do \"{genre.Label}\" ({genre.Meaning.TrimEnd('.')}). Remove it in the row over the cards.");
        }

        /// <summary>An archetype chip on the card detail was clicked: the search becomes that archetype's members.</summary>
        private void SearchArchetype(ushort code, string name)
        {
            ResetSearchForChip();
            _archetypePicks.Add((code, name));
            RunSearch();
            SetStatus($"{_results.Count} {name} cards. Remove it in the row over the cards.");
        }

        private void ResetSearchForChip()
        {
            _query = "";
            _searchField.SetTextWithoutNotify("");
            foreach (var c in _chips.ToList()) _chipImages[c].color = ChipColor(c, false);
            _chips.Clear();
            for (int i = 0; i < 4; i++) _picks[i] = 0;
            _related = null;
            ClearMoreFilters();
            if (_scope != ScopeOwned) _scope = ScopeAll;
            _seenDuel = -1;
        }

        // ---- For the search check (DuelController.SearchCheck) -------------------------------------------------

        public bool DebugMoreOpen => MoreOpen;
        public void DebugToggleMore() => ToggleMore();
        public void DebugGenre(string token) => ToggleGenre(token);
        public void DebugMatchAny(bool any) { _matchAny = any; RunSearch(); }
        public void DebugClearMore() { ClearMoreFilters(); RunSearch(); }
        public void DebugLimit(int limit) { _limit = limit; RunSearch(); }
        public void DebugLook(int look) { _look = look; RunSearch(); }
        public int MoreFilterCount => MoreCount;
        public int DebugLookIndex => _look;
        public string DebugLimitCaption => System.Text.RegularExpressions.Regex.Replace(_limitCaption.text, "<.*?>", "");
        public string DebugLimitLabel => _limitLabel.text;
        /// <summary>Where the close names start in the results (they're last), or -1.</summary>
        public int DebugCloseNamesFrom => _closeNamesFrom;
        public string DebugMoreButton => _moreLabel.text;

        /// <summary>Types into the Archetype box (as a player would) and picks the first suggestion; what was suggested.</summary>
        public List<string> DebugArchetype(string typed)
        {
            _archetypeField.text = typed; // fires the suggestions
            var shown = _suggestions.GetComponentsInChildren<Text>().Select(t => System.Text.RegularExpressions.Regex.Replace(t.text, "<.*?>", "")).ToList();
            var first = _suggestions.GetComponentsInChildren<Clickable>().FirstOrDefault();
            first?.Left?.Invoke();
            return shown;
        }

        /// <summary>The active row's chips as labels (the duel picker's too, first, in Seen in duels).</summary>
        public List<string> DebugActiveChips() =>
            _activeContent.GetComponentsInChildren<Text>().Select(t => t.text.Replace(" ×", "")).ToList();

        /// <summary>Clicks the active row's chip with this label (the ×); false when there's none.</summary>
        public bool DebugRemoveChip(string label)
        {
            foreach (Transform chip in _activeContent)
                if (chip.gameObject.activeSelf && chip.GetComponentInChildren<Text>()?.text == $"{label} ×" && chip.GetComponent<Clickable>() is Clickable c)
                {
                    c.Left?.Invoke();
                    return true;
                }
            return false;
        }

        /// <summary>The active row: how wide its chips run, and whether it can scroll (its window is 300 px).</summary>
        public (float width, bool scrolls) DebugActiveRow => (_activeContent.sizeDelta.x, _activeContent.sizeDelta.x > 300.5f);

        /// <summary>Fills the search box (as typing does) with the switch at <paramref name="mode"/>; the results.</summary>
        public List<CardInfo> DebugSearch(string query, CardSearch.SearchMode mode = CardSearch.SearchMode.NameAndText)
        {
            _searchMode = mode;
            if (_searchField.text == query) RunSearch();
            else _searchField.text = query; // triggers the search
            return _results.ToList();
        }

        /// <summary>Clicks the search box's switch once (Name + text, Name, Text); the label it then shows.</summary>
        public string DebugCycleMode()
        {
            CycleMode(+1);
            return _modeLabel.text;
        }

        public List<CardInfo> DebugResults => _results.ToList();
        public int DebugScopeIndex => _scope;
        public string DebugScopeLabel => _ownedLabel.text;
        public string DebugEmptyNote => _emptyNote.text;
        public SeenLog DebugSeenLog => _seenLog;

        /// <summary>Clicks the scope button until it shows <paramref name="scope"/> (0 all, 1 owned, 2 favorites, 3 seen, 4 viewed); false if it never does.</summary>
        public bool DebugScope(int scope)
        {
            for (int i = 0; i < 6 && _scope != scope; i++) CycleScope(+1);
            return _scope == scope;
        }

        /// <summary>Clicks the duel picker (right-click with a negative step); its label.</summary>
        public string DebugSeenDuel(int delta)
        {
            CycleSeenDuel(delta);
            return SeenDuelLabel(_seenDuel);
        }

        /// <summary>A card picked as a click on a search result does (it's added to the deck, and goes to Recently viewed).</summary>
        public void DebugSelect(uint code)
        {
            if (_cards.Get(code) is CardInfo info) Add(info, side: false);
        }

        /// <summary>The lines under the first page's cards ("vs Professor Mica", "own 2"...), without markup.</summary>
        public List<string> DebugUnderLines() =>
            _resultsLayer.GetComponentsInChildren<Text>().Where(t => t.transform.parent?.name == "Under" || t.name == "Under")
                .Select(t => System.Text.RegularExpressions.Regex.Replace(t.text, "<.*?>", "")).ToList();

        /// <summary>Picks the last ban list whose name passes <paramref name="match"/> (as the list arrows would); its name, or null.</summary>
        public string? DebugBanlist(Func<Banlist, bool> match)
        {
            int at = _banlists.FindLastIndex(b => match(b));
            if (at < 0) return null;
            CycleBanlist(at - _banIndex);
            return _banlists[_banIndex].Name;
        }

        public Banlist DebugList => _banlists[_banIndex];
        public CardSearch DebugCardSearch => _search;

        /// <summary>The card detail, showing a card as a hover does.</summary>
        public CardDetail DebugShow(CardInfo info)
        {
            ShowDetail(info);
            return _cardDetail;
        }

        /// <summary>The search panel's controls (and, while it's open, the More panel's), in 1920x1080 layout pixels from the top-left.</summary>
        public List<(string what, Rect rect)> DebugSearchBoxes(bool more)
        {
            var corners = new Vector3[4];
            Rect Of(RectTransform rt)
            {
                rt.GetWorldCorners(corners);
                var a = _root.InverseTransformPoint(corners[0]);
                var b = _root.InverseTransformPoint(corners[2]);
                var size = _root.rect.size;
                // _root's local space is centred; the layout counts from its top-left.
                return Rect.MinMaxRect(a.x + size.x / 2, size.y / 2 - b.y, b.x + size.x / 2, size.y / 2 - a.y);
            }
            if (!more) return _searchLayout.Select(b => (b.what, Of(b.rt))).ToList();
            var boxes = new List<(string, Rect)>();
            foreach (Transform child in _more)
            {
                if (!child.gameObject.activeSelf || child == _suggestions) continue;
                var rt = (RectTransform)child;
                var name = child.GetComponentInChildren<Text>()?.text ?? child.name;
                boxes.Add((name.Replace("\n", " "), Of(rt)));
            }
            return boxes;
        }

        /// <summary>The Text components in the search panel and the More panel (for the check's text-fit test).</summary>
        public List<Text> DebugSearchTexts() =>
            _searchLayout.Where(b => b.what != "results grid").SelectMany(b => b.rt.GetComponentsInChildren<Text>())
                .Concat(_activeContent.GetComponentsInChildren<Text>())
                .Concat(MoreOpen ? _more.GetComponentsInChildren<Text>() : Enumerable.Empty<Text>()).ToList();
    }
}
