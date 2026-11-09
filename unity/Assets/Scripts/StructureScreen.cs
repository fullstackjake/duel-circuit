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
    /// The Card Shop's structure decks: ready-to-play themed decks for DP. Buying one adds its cards to your collection
    /// (3 of a card at most; extras turn into DP) and saves it as a campaign deck you can use and edit right away. New
    /// ones go on sale as the shop's era advances.
    /// </summary>
    public sealed class StructureScreen : MonoBehaviour
    {
        private CampaignState _state = null!;
        private DeckLibrary _library = null!;
        private ICardDataProvider _cards = null!;
        private RectTransform _root = null!, _list = null!;
        private Text _header = null!, _status = null!;

        public event Action? Closed;
        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Init(CampaignState state, DeckLibrary library, ICardDataProvider cards)
        {
            _state = state;
            _library = library;
            _cards = cards;
            _root = Ui.Overlay(transform, "Structure", 15);
            Ui.Image(_root, Ui.Hex("#121620"));
            Ui.Image(Ui.Box(_root, 0, 0, 1920, 90), Ui.Hex("#0D1017"));
            Ui.Text(Ui.Box(_root, 40, 0, 600, 90), "STRUCTURE DECKS", 34, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _header = Ui.Text(Ui.Box(_root, 520, 0, 1000, 90), "", 24, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
            Ui.BoxButton(_root, 1720, 22, 160, 46, "Back", Close, 18);
            Ui.Text(Ui.Box(_root, 40, 100, 1840, 40),
                "Ready-to-play themed decks. Buying one adds its cards to your collection and saves it as a campaign deck. More go on sale as the shop's era advances.",
                17, new Color(0.75f, 0.78f, 0.86f), TextAnchor.MiddleLeft);
            _list = Ui.Box(_root, 40, 150, 1840, 850);
            _status = Ui.Text(Ui.Box(_root, 40, 1010, 1840, 50), "", 16, new Color(0.8f, 0.83f, 0.9f), TextAnchor.MiddleLeft);
            _status.supportRichText = true;
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            _library.Refresh();
            Show();
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private static string Title(string name, Deck deck) => deck.Note("title").Length > 0 ? deck.Note("title")
            : string.Join(" ", name.Split('-').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w));

        private static int Number(string text, int fallback) => int.TryParse(text, out var n) ? n : fallback;

        private void Show()
        {
            for (int i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            var profile = _state.Profile!;
            _header.text = $"{profile.Dp:N0} DP";
            int year = _state.Circuits.ShopYear(profile);
            var decks = _library.StructureNames.Select(n => (name: n, deck: _library.Load(n)))
                .OrderBy(d => Number(d.deck.Note("era"), 0)).ThenBy(d => d.name).ToList();
            for (int i = 0; i < decks.Count; i++)
            {
                var (name, deck) = decks[i];
                int era = Number(deck.Note("era"), 0), price = Number(deck.Note("price"), 1500);
                bool open = era <= year;
                int col = i % 2, row = i / 2;
                var tile = Ui.Box(_list, col * 930, row * 210, 900, 196);
                Ui.Image(tile, open ? Ui.Hex("#1C2232") : Ui.Hex("#15181F"));
                // Its signature card.
                var face = CampaignScenes.ShowcaseOf(deck, _cards).FirstOrDefault();
                var art = face == 0 ? null : CardView.Art?.Get(face);
                if (art != null)
                {
                    var img = Ui.Box(tile, 12, 12, 118, 172).gameObject.AddComponent<RawImage>();
                    img.texture = art;
                    img.color = open ? Color.white : new Color(1, 1, 1, 0.3f);
                }
                var title = Title(name, deck);
                float dim = open ? 1 : 0.4f;
                Ui.Text(Ui.Box(tile, 146, 12, 560, 36), title, 24, new Color(1, 1, 1, dim), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(tile, 146, 48, 560, 26), open ? $"{era} · {deck.Main.Count} main · {deck.Extra.Count} extra" : $"On sale when the shop reaches {era}",
                    15, new Color(0.7f, 0.74f, 0.82f, dim), TextAnchor.MiddleLeft);
                Ui.Text(Ui.Box(tile, 146, 78, 730, 110), deck.Note("about"), 15, new Color(0.88f, 0.9f, 0.95f, dim), TextAnchor.UpperLeft);
                if (open)
                {
                    var n = name;
                    var d = deck;
                    Ui.BoxButton(tile, 720, 14, 166, 50, $"Buy · {price:N0} DP", () => Buy(n, d, price), 17).targetGraphic.color = Ui.Hex("#B8432F");
                }
            }
        }

        private void Buy(string name, Deck deck, int price)
        {
            var profile = _state.Profile!;
            if (profile.Dp < price)
            {
                Sounds.Play("error");
                _status.text = $"<color=#FF8A8A>Not enough DP:</color> it costs {price:N0}, you have {profile.Dp:N0}.";
                return;
            }
            // Its cards are the deck's set's printings when the file names one, else shop copies by the shop's year (spec 4.2).
            int kept = profile.BuyStructureDeck(deck, price, _cards, _state.ShopYear);
            // Saved as a campaign deck you can pick and edit.
            var copy = deck.Clone();
            copy.Name = DeckLibrary.SafeName(Title(name, deck));
            copy.Campaign = true;
            copy.Notes.Remove("price");
            _library.Save(copy);
            _library.Refresh();
            _state.Save();
            Sounds.Play("burst");
            _status.text = $"<color=#7BD88F>Bought {copy.Name}!</color> {kept} cards added to your collection; it's in your campaign decks now.";
            // A set it completed: Gus frames a card (card unlocks).
            if (_state.CheckSets() is { Length: > 0 } sets) _status.text += $"  <color={UiTheme.Current.GoldSoftHex}>{sets}</color>";
            LastBought = copy.Name;
            Show();
        }

        /// <summary>For automated checks: the decks shown, and buying the first one on sale.</summary>
        public string LastBought { get; private set; } = "";
        public int DebugOnSale() => _library.StructureNames.Count(n => Number(_library.Load(n).Note("era"), 0) <= _state.Circuits.ShopYear(_state.Profile!));
        public void DebugBuyFirst()
        {
            var name = _library.StructureNames.First(n => Number(_library.Load(n).Note("era"), 0) <= _state.Circuits.ShopYear(_state.Profile!));
            var deck = _library.Load(name);
            Buy(name, deck, Number(deck.Note("price"), 1500));
        }
    }
}
