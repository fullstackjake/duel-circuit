using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The card reader: a card's full details over the dimmed duel, the card big on the left and its name, stats, type
    /// and text on the right, with a close button at the top right. Opened with a card's Details button, the side
    /// panel's Details button (or a click on its card), or D for the card the side panel shows. Its Artwork button picks
    /// the card's art style.
    /// </summary>
    public sealed partial class BoardView
    {
        private RectTransform? _reader;
        private CardDetail? _readerDetail;

        /// <summary>Whether the reader is open, and what it shows (for checks).</summary>
        public bool ReaderOpen => _reader != null;
        public CardDetail? ReaderDetail => _readerDetail;

        public void OpenReader(CardInfo? info)
        {
            if (info == null) return;
            CloseActionMenu();
            CloseReader();
            var root = Ui.Stretched("CardReader", _canvas, Vector2.zero, Vector2.one);
            _reader = root;
            var dim = Ui.Image(root, new Color(0, 0, 0, 0.78f));
            Ui.Clickable((RectTransform)dim.transform, CloseReader);
            var panel = Ui.Placed("Panel", root, new Vector2(0, -20), new Vector2(1560, 820));
            Ui.Image(panel, new Color(0, 0, 0, 0.001f)); // a click on the panel's empty parts doesn't close it
            var inner = Ui.Stretched("Detail", panel, Vector2.zero, Vector2.one);
            _readerDetail = new CardDetail(inner, reading: true) { StylePicker = true };
            _readerDetail.Show(info);
            _readerStyle = CardView.Art?.StyleOf(info.Code);
            Canvas.ForceUpdateCanvases();
            _readerDetail.Layout();

            // Close: a ring with an X at the top right of the screen.
            var close = Ui.Rect("Close", root);
            close.anchorMin = close.anchorMax = new Vector2(1, 1);
            close.anchoredPosition = new Vector2(-64, -60);
            close.sizeDelta = new Vector2(76, 76);
            var ring = close.gameObject.AddComponent<RawImage>();
            ring.texture = BoardFx.Textures.Ring;
            ring.color = Ui.Hex("#FFC940");
            var x = Ui.Placed("X", close, Vector2.zero, new Vector2(52, 52)).gameObject.AddComponent<RawImage>();
            x.texture = Icons.For("cancel");
            x.color = Ui.Hex("#FFC940");
            x.raycastTarget = false;
            Ui.Clickable(close, () => { Sounds.Play("click"); CloseReader(); });
            var tip = close.gameObject.AddComponent<TipOnHover>();
            tip.Title = "Close";
            tip.Body = "Back to the duel (Esc).";
            root.SetAsLastSibling();
        }

        public void CloseReader()
        {
            TermTooltip.Hide();
            // Its Artwork button changed the card's picture: the board and the side panel draw it in the new style.
            if (_readerDetail?.Shown is { } shown && CardView.Art?.StyleOf(shown.Code) != _readerStyle)
            {
                if (_lastBoard != null) Render(_lastBoard);
                if (_detail.Shown != null) _detail.Show(_detail.Shown);
            }
            if (_reader != null) Destroy(_reader.gameObject);
            _reader = null;
            _readerDetail = null;
        }
        private string? _readerStyle;

        /// <summary>D: the reader for the card the side panel shows, or closes it.</summary>
        private void ToggleReader()
        {
            if (_reader != null) CloseReader();
            else OpenReader(_detail.Shown);
        }

        /// <summary>The card at a place if you may read it (face-up, yours, or a hand that's shown), else null.</summary>
        private CardInfo? ReadableAt(int controller, uint location, int sequence)
        {
            if (_lastBoard == null || controller < 0 || controller > 1 || sequence < 0) return null;
            var player = _lastBoard.Players[controller];
            bool reveal = controller != HiddenSeat;
            FieldCard? card = (location & 0x7f) switch
            {
                Location.MZone => sequence < player.Monsters.Count ? player.Monsters[sequence] : null,
                Location.SZone => sequence < player.SpellTraps.Count ? player.SpellTraps[sequence] : null,
                Location.Hand => sequence < player.Hand.Count && HandShown(controller) && reveal ? player.Hand[sequence] : null,
                _ => null,
            };
            if (card == null || card.Code == 0) return null;
            if ((location & 0x7f) != Location.Hand && !card.IsFaceUp && !reveal) return null;
            return _cards.Get(card.Code);
        }

        /// <summary>For checks: the reader as the Details button opens it for a card on the board.</summary>
        public bool DebugReadCard(int controller, uint location, int sequence)
        {
            OpenReader(ReadableAt(controller, location, sequence));
            return _reader != null;
        }
    }
}
