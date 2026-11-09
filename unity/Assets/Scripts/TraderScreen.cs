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
    /// The Card Trader (opened from the Card Shop): spend a Card Token, earned by levelling up, on one copy of any card.
    /// With no search, your wishlist's missing cards are listed first. In an era-limited campaign only cards released up
    /// to the shop's era can be traded for. Opened from a Library cup it's the Library Trader: only cards out by the cup's
    /// pool month are listed or traded, whatever the mode, each with its place on the cup's list.
    /// </summary>
    public sealed class TraderScreen : MonoBehaviour
    {
        private const int PerPage = 12;

        private CampaignState _state = null!;
        private ICardDataProvider _cards = null!;
        private CardSearch _search = null!;
        private RectTransform _root = null!, _list = null!, _detail = null!;
        private Text _title = null!, _header = null!, _status = null!, _pageLabel = null!;
        private Button _buyToken = null!;
        private List<CardInfo> _shown = new List<CardInfo>();
        private CardInfo? _selected;
        private string _query = "";
        private int _page;
        // OCG-only: just the cards the TCG never printed (the shop's import shelf's cards); not offered in era-limited play.
        private bool _ocgOnly;
        // The Library Trader never lists OCG-only cards; the shop's toggle keeps its setting for the next visit there.
        private bool OcgOnly => _ocgOnly && _rules == null;
        private Button _ocgToggle = null!;
        // The Library Trader: the cup's rules and name (null and "" at the shop's trader).
        private FormatRules? _rules;
        private string _cup = "";
        private string Pool => _rules?.Pool ?? "";

        public event Action? Closed;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Init(CampaignState state, ICardDataProvider cards)
        {
            _state = state;
            _cards = cards;
            _search = new CardSearch(cards);
            Build();
            _root.gameObject.SetActive(false);
        }

        private int ShopYear => _state.Profile == null ? 2005 : _state.Circuits.ShopYear(_state.Profile);

        /// <summary>
        /// The shop's way trades any era, so a card first printed after the shop's current era says it's ahead of your band
        /// (Historic Rules refuse it instead).
        /// </summary>
        private bool AheadOfBand(CardInfo info) =>
            _rules == null && !_state.Profile!.EraLimited && (CardFoil.Printings?.FirstYear(info.Code, info.Alias) ?? 0) > ShopYear;
        private static string AheadTag => UiTheme.Current.Paint("ahead of your band", UiTheme.Current.Warn);

        private void Build()
        {
            var T = UiTheme.Current;
            _root = Ui.Overlay(transform, "Trader", 15);
            Ui.Image(_root, T.Base);
            Ui.Image(Ui.Box(_root, 0, 0, 1920, 90), T.Deep);
            _title = Ui.Text(Ui.Box(_root, 40, 0, 400, 90), "CARD TRADER", 34, T.Sign, TextAnchor.MiddleLeft);
            _title.fontStyle = FontStyle.Bold;
            _header = Ui.Text(Ui.Box(_root, 380, 0, 780, 90), "", 22, T.GoldSoft, TextAnchor.MiddleLeft);
            _header.supportRichText = true;
            // DP buys tokens too, dearer each time.
            _buyToken = Ui.BoxButton(_root, 1450, 22, 260, 46, "", BuyToken, 16);
            Ui.BoxButton(_root, 1720, 22, 160, 46, "Back", Close, 18);

            Ui.Input(Ui.Box(_root, 40, 110, 640, 44), "Search any card: name, text, lv8, atk>2000, dragon, \"destroy all\"...", 16, v => { _query = v.Trim(); _page = 0; Find(); });
            _ocgToggle = Ui.BoxButton(_root, 690, 110, 170, 44, "", () => { _ocgOnly = !_ocgOnly; _page = 0; Find(); }, 16);
            _list = Ui.Box(_root, 40, 166, 820, PerPage * 62);
            Ui.BoxButton(_root, 40, 950, 140, 40, "< Prev", () => { _page--; ShowList(); });
            _pageLabel = Ui.Text(Ui.Box(_root, 190, 950, 480, 40), "", 16, T.Text);
            Ui.BoxButton(_root, 720, 950, 140, 40, "Next >", () => { _page++; ShowList(); });
            _detail = Ui.Box(_root, 900, 110, 980, 880);
            Ui.Image(_detail, T.Panel);
            _status = Ui.Text(Ui.Box(_root, 40, 1010, 1840, 50), "", 16, T.TextSoft, TextAnchor.MiddleLeft);
            _status.supportRichText = true;
        }

        public void Open() => Open(null, "");

        /// <summary>The trader under a Library cup's rules (<paramref name="cup"/> its name), or the shop's (null).</summary>
        public void Open(FormatRules? rules, string cup)
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _page = 0;
            var newRules = rules != null && rules.Pool.Length > 0 ? rules : null;
            // A card picked at another trader (or another cup's) may be out of this one's pool: start with none picked.
            if (newRules?.Pool != _rules?.Pool || (newRules == null) != (_rules == null)) _selected = null;
            _rules = newRules;
            _cup = _rules != null ? cup : "";
            _title.text = _rules != null ? "LIBRARY TRADER" : "CARD TRADER";
            // OCG-only cards were never in a TCG format: no filter for them in era-limited play or at the Library.
            _ocgToggle.gameObject.SetActive(!_state.Profile!.EraLimited && _rules == null && _state.Imports.Cards.Count > 0);
            if (_state.Profile!.EraLimited) _ocgOnly = false;
            Find();
            const string Prices = "A card costs 1 token if it was ever printed as a Common or Rare, 2 if a Super or Ultra Rare at best, 3 if only ever Secret or rarer.";
            _status.text = _rules != null
                ? $"{_cup}: Card Tokens trade only for cards out by {DeckRules.MonthYear(Pool)}, whatever your campaign's mode. {Prices}"
                : _state.Profile!.EraLimited
                ? $"Card Tokens trade for one copy of a card, at its least rare printing. {Prices} Historic Rules: cards released up to {ShopYear}."
                : $"Card Tokens trade for one copy of any card of any era, at its least rare printing. {Prices}";
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void Find()
        {
            var profile = _state.Profile!;
            _ocgToggle.GetComponentInChildren<Text>().text = _ocgOnly ? "OCG-only: on" : "OCG-only: off";
            if (OcgOnly)
            {
                // Every OCG-only card, or the ones the search finds.
                _shown = (_query.Length == 0 ? _state.Imports.Cards.Select(_cards.Get).OfType<CardInfo>() : _search.Find(_query, CardSearch.SearchMode.NameAndText, "", 2000, closeNames: false))
                    .Where(c => _state.Imports.IsOcgOnly(c.Code)).OrderBy(c => c.Name).ToList();
            }
            else if (_query.Length == 0)
            {
                // Your wishlist's missing cards first.
                _shown = profile.Wanted().Select(_cards.Get).Where(c => c != null).Select(c => c!).OrderBy(c => c.Name).ToList();
            }
            else _shown = _search.Find(_query, CardSearch.SearchMode.NameAndText, "", 600, closeNames: false); // (no close names: a trade list is exact)
            // The Library Trader lists only the cup's pool.
            if (_rules != null && CardFoil.Printings is PrintingIndex printings)
                _shown = _shown.Where(c => CardTrader.InPool(c.Code, Pool, printings, _cards)).ToList();
            ShowList();
            if (_shown.Count > 0 && (_selected == null || !_shown.Contains(_selected))) Select(_shown[0]);
            else if (_selected != null) Select(_selected);
            else ClearDetail();
        }

        private void ShowHeader()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var (into, needed) = Leveling.Progress(profile.Exp);
            _header.text = $"{profile.Tokens} Card Token{(profile.Tokens == 1 ? "" : "s")}   <color={T.GoldHex}><size=18>{profile.Dp:N0} DP</size></color>   "
                           + $"<color={T.TextDimHex}><size=16>Level {profile.Level} ({(profile.Level >= Leveling.MaxLevel ? "max level" : $"{into}/{needed} EXP")}) · {profile.TokensSpent} traded</size></color>";
            _buyToken.GetComponentInChildren<Text>().text = $"Buy a token · {DpServices.TokenPrice(profile):N0} DP";
            _buyToken.interactable = profile.Dp >= DpServices.TokenPrice(profile);
        }

        private void BuyToken()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            int price = DpServices.TokenPrice(profile);
            if (!DpServices.BuyToken(profile)) { Sounds.Play("error"); _status.text = $"<color={T.BadHex}>A token costs {price:N0} DP; you have {profile.Dp:N0}.</color>"; return; }
            Sounds.Play("token");
            _state.Save();
            _status.text = $"<color={T.GoodHex}>Bought a Card Token for {price:N0} DP.</color> The next costs {DpServices.TokenPrice(profile):N0} DP.";
            ShowList();
            if (_selected != null) Select(_selected);
        }

        /// <summary>For automated play: "Buy a token", as clicked; the status line after.</summary>
        public string DebugBuyToken() { BuyToken(); return _status.text; }

        /// <summary>For automated checks: the DP button, as clicked, on an opened Trader. The status line after.</summary>
        public string DebugBuy()
        {
            Open();
            BuyToken();
            return _status.text;
        }

        private void ShowList()
        {
            var T = UiTheme.Current;
            ShowHeader();
            for (int i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            int pages = Math.Max(1, (_shown.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = $"{_page + 1} / {pages}   ({_shown.Count} {(OcgOnly ? "OCG-only card" : "card")}{(_shown.Count == 1 ? "" : "s")}{(_query.Length == 0 && !OcgOnly ? " on your wishlist" : "")}{(_rules != null ? $" out by {DeckRules.MonthYear(Pool)}" : "")})";
            if (_shown.Count == 0)
            {
                Ui.Text(Ui.Box(_list, 0, 0, 820, 120), _query.Length == 0 && _rules != null
                    ? $"None of your wishlist's cards were out by {DeckRules.MonthYear(Pool)}. Search for a card from the {_cup}'s era above."
                    : _query.Length == 0
                    ? "Your wishlist is empty. Search for any card above, or use Recipe in the campaign's deck editor to fill the wishlist."
                    : _rules != null ? $"No cards out by {DeckRules.MonthYear(Pool)} match." : "No cards match.", 17, T.TextMuted, TextAnchor.MiddleLeft);
                return;
            }
            var profile = _state.Profile!;
            for (int row = 0; row < PerPage; row++)
            {
                int i = _page * PerPage + row;
                if (i >= _shown.Count) break;
                var info = _shown[i];
                var rt = Ui.Box(_list, 0, row * 62, 820, 58);
                Ui.Image(rt, info == _selected ? T.RowSelected : row % 2 == 0 ? T.RowA : T.RowB);
                var art = CardView.Art?.Get(info.Code);
                var thumb = Ui.Box(rt, 4, 4, 36, 50);
                if (art != null) thumb.gameObject.AddComponent<RawImage>().texture = art;
                else Ui.Image(thumb, T.Button); // no art yet: the button fill, as before
                Ui.Text(Ui.Box(rt, 50, 4, 560, 26), info.Name, 16, T.Text, TextAnchor.MiddleLeft);
                if (_state.Imports.IsOcgOnly(info.Code)) OcgBadge(rt, 750, 6);
                var blocked = CardTrader.Blocked(info.Code, profile, ShopYear, CardFoil.Printings, _cards, Pool);
                var owned = profile.Collection.Count(info.Code);
                int cost = CardTrader.Cost(info.Code, CardFoil.Printings, _cards);
                var line = $"you own {owned} · <color={T.GoldHex}>{cost} token{(cost == 1 ? "" : "s")}</color>" + (blocked == null ? $" · <color={T.GoodHex}>can trade</color>"
                    : blocked.StartsWith("you have no") || blocked.StartsWith("it costs") ? "" : $" · <color={T.BadHex}>{blocked}</color>")
                    + (AheadOfBand(info) ? $" · {AheadTag}" : "") + ListTag(info, " · ");
                Ui.Text(Ui.Box(rt, 50, 30, 760, 24), line, 13, T.TextFaint, TextAnchor.MiddleLeft).supportRichText = true;
                var pick = info;
                int at = i;
                Ui.Clickable(rt, () => Select(pick), hover: () => _keys.Point(at), style: HoverStyle.Row);
            }
        }

        /// <summary>At the Library Trader, a card's place on the cup's list ("Limited in TCG December 2024"), after <paramref name="lead"/>; "" when unlimited.</summary>
        private string ListTag(CardInfo info, string lead)
        {
            if (_rules?.List == null) return "";
            int allowed = _rules.List.Allowed(info.Code, _cards);
            var label = allowed == 0 ? "Forbidden" : allowed == 1 ? "Limited" : allowed == 2 ? "Semi-Limited" : "";
            return label.Length == 0 ? "" : $"{lead}<color={UiTheme.Current.BadHex}>{label} in {_rules.List.Name}</color>";
        }

        /// <summary>The OCG badge: a small orange "OCG" chip at (x, y) in <paramref name="parent"/>.</summary>
        private static void OcgBadge(RectTransform parent, float x, float y)
        {
            var T = UiTheme.Current;
            var chip = Ui.Box(parent, x, y, 56, 22);
            Ui.Image(chip, T.ImportChip).raycastTarget = false;
            var label = Ui.Text(chip, "OCG", 13, T.Text);
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;
        }

        /// <summary>For checks: the OCG-only filter switched on (or off), with no search; the cards it shows.</summary>
        public List<CardInfo> DebugOcgOnly(bool on)
        {
            Open();
            _query = "";
            _ocgOnly = on;
            Find();
            return _shown;
        }

        /// <summary>For checks: whether the OCG-only filter is offered.</summary>
        public bool OcgFilterShown => _ocgToggle.gameObject.activeSelf;

        private void ClearDetail()
        {
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
        }

        private void Select(CardInfo info)
        {
            var T = UiTheme.Current;
            _selected = info;
            ShowList();
            ClearDetail();
            var profile = _state.Profile!;
            var printings = CardFoil.Printings;
            var art = CardView.Art?.Get(info.Code);
            var face = Ui.Box(_detail, 30, 30, 300, 438);
            Ui.Image(face, T.Well);
            if (art != null) Ui.Stretched("Art", face, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>().texture = art;
            Ui.Text(Ui.Box(_detail, 360, 30, 590, 44), info.Name, 26, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            int year = printings?.FirstYear(info.Code, info.Alias) ?? 0;
            var rarity = printings?.Commonest(info.Code, info.Alias) ?? Rarity.Common;
            bool ocg = _state.Imports.IsOcgOnly(info.Code);
            if (ocg) OcgBadge(_detail, 30, 478); // under the card picture
            var facts = Ui.Text(Ui.Box(_detail, 360, 78, 590, 60),
                $"{(year > 0 ? $"First printed {year}" : ocg ? "OCG-only: printed in Japan and Asia, never in the TCG" : "Not printed in the TCG")}{(AheadOfBand(info) ? $" ({AheadTag}: the shop's at {ShopYear})" : "")}{ListTag(info, " · ")} · you'd get it as {CardFoil.ColoredName(rarity)}\nYou own {profile.Collection.Count(info.Code)} of {Collection.MaxCopies}",
                16, T.TextSoft, TextAnchor.UpperLeft);
            facts.supportRichText = true;
            var text = Ui.Text(Ui.Box(_detail, 360, 150, 590, 320), info.Text, 15, T.TextBody, TextAnchor.UpperLeft);
            text.verticalOverflow = VerticalWrapMode.Truncate;

            var blocked = CardTrader.Blocked(info.Code, profile, ShopYear, printings, _cards, Pool);
            int cost = CardTrader.Cost(info.Code, printings, _cards);
            var button = Ui.BoxButton(_detail, 30, 520, 920, 80, blocked == null ? $"Trade {cost} Card Token{(cost == 1 ? "" : "s")} for it" : $"Can't trade: {blocked}", () => Trade(info), 24);
            button.interactable = blocked == null;
            if (blocked == null) button.targetGraphic.color = T.Primary; // the screen's commit action
        }

        private void Trade(CardInfo info)
        {
            var profile = _state.Profile!;
            var T = UiTheme.Current;
            int cost = CardTrader.Cost(info.Code, CardFoil.Printings, _cards);
            if (!CardTrader.Trade(info.Code, profile, ShopYear, CardFoil.Printings, _cards, Pool)) { Sounds.Play("error"); return; }
            Sounds.Play("token");
            _state.Save();
            _status.text = $"<color={T.GoodHex}>Traded {(cost == 1 ? "a Card Token" : $"{cost} Card Tokens")} for {info.Name}.</color> You now own {profile.Collection.Count(info.Code)}; "
                           + $"{profile.Tokens} token{(profile.Tokens == 1 ? "" : "s")} left.";
            // A set it completed: Gus frames a card (card unlocks).
            if (_state.CheckSets() is { Length: > 0 } sets) _status.text += $"  <color={T.GoldSoftHex}>{sets}</color>";
            if (_query.Length == 0) Find(); // the wishlist may have shrunk
            else Select(info);
        }

        /// <summary>For automated checks: trades a token for the first card that can be traded for; its name, or null.</summary>
        public string? DebugTradeFirst()
        {
            Open();
            var target = _shown.FirstOrDefault(c => CardTrader.Blocked(c.Code, _state.Profile!, ShopYear, CardFoil.Printings, _cards, Pool) == null);
            if (target == null) return null;
            Select(target);
            Trade(target);
            return target.Name;
        }

        /// <summary>
        /// For automated play: searches the trader for each card in turn and trades a token for the first it allows,
        /// with the Trade button. The card traded for (null if none could be), and why the ones before it couldn't.
        /// </summary>
        public (CardInfo? card, List<string> refused) DebugTradeFor(IEnumerable<uint> codes)
        {
            Open();
            var refused = new List<string>();
            foreach (var code in codes)
            {
                var info = _cards.Get(code);
                if (info == null) continue;
                var blocked = CardTrader.Blocked(code, _state.Profile!, ShopYear, CardFoil.Printings, _cards, Pool);
                if (blocked != null) { refused.Add($"{info.Name}: {blocked}"); continue; }
                _query = info.Name;
                Find();
                Select(info);
                Trade(info);
                return (info, refused);
            }
            return (null, refused);
        }

        /// <summary>For checks: the Library Trader's reason a card can't be traded for (null when it can), as its rows say.</summary>
        public string? DebugBlocked(uint code) => CardTrader.Blocked(code, _state.Profile!, ShopYear, CardFoil.Printings, _cards, Pool);

        // ---- The keyboard ------------------------------------------------------------------------------

        // Up/down (or the d-pad) move the highlight through the cards (across pages), left/right turn the page. Enter on the
        // highlighted card asks "Trade N Card Tokens for X?" (KeyConfirm: a second Enter trades as the Trade button does,
        // Escape cancels; one press never trades); a card that can't be traded for just shows why (ArrowNav).
        private readonly ArrowNav _keys = new ArrowNav();
        private readonly KeyConfirm _confirm = new KeyConfirm();

        private void Update()
        {
            if (!IsOpen) { _confirm.Close(); return; }
            ArrowNav.DropButtonFocus(_root); // a clicked Trade button mustn't take Enter
            if (_confirm.Update()) return;
            var k = _keys.Read(_root, _list);
            if (!k.Any || _shown.Count == 0) return;
            int at = _selected == null ? -1 : _shown.IndexOf(_selected);
            if (k.Move != Vector2Int.zero) at = _keys.From(at, _shown.Count); // on from the row the mouse last pointed at
            int to = ArrowNav.ListStep(_shown.Count, PerPage, _page, at, k.Move, _ => true);
            if (to >= 0) { _page = to / PerPage; Select(_shown[to]); }
            else if (k.Enter && at >= 0 && at / PerPage == _page) AskToTrade(_shown[at]);
        }

        /// <summary>Enter on a card: the trade asked first (KeyConfirm), then done as the Trade button does; a blocked card just selected.</summary>
        private void AskToTrade(CardInfo info)
        {
            if (_selected != info) Select(info);
            if (CardTrader.Blocked(info.Code, _state.Profile!, ShopYear, CardFoil.Printings, _cards, Pool) != null) return;
            int cost = CardTrader.Cost(info.Code, CardFoil.Printings, _cards);
            _confirm.Open(_root, $"Trade {(cost == 1 ? "a Card Token" : $"{cost} Card Tokens")} for <b>{info.Name}</b>?", "Trade", () => Trade(info));
        }

        /// <summary>For checks: selects (as a click) the first card shown that can be traded for; its name, or null.</summary>
        public string? DebugSelectTradable()
        {
            var target = _shown.FirstOrDefault(c => CardTrader.Blocked(c.Code, _state.Profile!, ShopYear, CardFoil.Printings, _cards, Pool) == null);
            if (target == null) return null;
            _page = _shown.IndexOf(target) / PerPage;
            Select(target);
            return target.Name;
        }

        /// <summary>For checks: the keyboard's trade question as shown ("" when none is up).</summary>
        public string DebugConfirm => _confirm.Question;

        /// <summary>For checks: the card selected (its name), or "".</summary>
        public string DebugSelected => _selected?.Name ?? "";

        /// <summary>For checks: the title over the trader ("CARD TRADER", or "LIBRARY TRADER" for a cup).</summary>
        public string Title => _title.text;

        /// <summary>For automated checks: searches the trader.</summary>
        public List<CardInfo> DebugSearch(string query)
        {
            _query = query;
            Find();
            return _shown;
        }
    }
}
