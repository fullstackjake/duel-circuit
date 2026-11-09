using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;
using Ygo.Engine.Protocol;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Column guides: while the card you're reading (or placing) is about columns, the five columns show on the field,
    /// each with how many cards are in it. For "if 2 or more cards are in the same column" the columns that have them
    /// glow gold; for a Mekk-Knight, only those where your Main Monster Zone is free too, and "summon here" while you
    /// could Summon it now (your Main Phase), a plain check mark otherwise (their turn, your Battle or End Phase).
    /// </summary>
    public sealed partial class BoardView
    {
        private RectTransform? _columnLayer;
        private (uint code, int version, bool now) _columnsDrawn;
        /// <summary>Bumped on every redraw, so the guide follows the board.</summary>
        private int _boardVersion;
        private readonly int[] _columnCounts = new int[Columns.Count];
        private readonly bool[] _columnReady = new bool[Columns.Count];

        /// <summary>For checks: the card the guides are for (null when they're off), and each column's count and gold.</summary>
        public (string? card, int[] counts, bool[] ready) ColumnGuide =>
            (_columnsDrawn.code == 0 ? null : _cards.Get(_columnsDrawn.code)?.Name, (int[])_columnCounts.Clone(), (bool[])_columnReady.Clone());

        /// <summary>For checks: the guides' count labels as drawn, left to right ("2 · summon here", "2 ✓", "1").</summary>
        public System.Collections.Generic.List<string> ColumnLabels { get; } = new System.Collections.Generic.List<string>();

        /// <summary>
        /// Whether you could Summon a card now, as far as the board tells: your turn (the near seat's when watching) and a
        /// Main Phase. Not on their turn, in your Battle Phase or End Phase, where "summon here" would be a false promise.
        /// </summary>
        private bool CanSummonNow(BoardState board) =>
            board.TurnPlayer == (HumanSeat >= 0 ? HumanSeat : 0) && (board.Phase == Phase.Main1 || board.Phase == Phase.Main2);

        /// <summary>The column card in focus: the one you're placing, else the one you're pointing at, else the detail's.</summary>
        private CardInfo? ColumnCard()
        {
            if (Prompt.Current?.Prompt is PlacePrompt && _focusCard is { } f && CodeAt(f.controller, f.location, f.sequence) is uint code
                && _cards.Get(code) is { } placing && Columns.Mentions(placing))
                return placing;
            if (_hoverRoot.gameObject.activeSelf && Columns.Mentions(_hoverDetail.Shown)) return _hoverDetail.Shown;
            return Columns.Mentions(_detail.Shown) ? _detail.Shown : null;
        }

        private uint? CodeAt(int controller, uint location, int sequence)
        {
            if (_lastBoard == null || controller > 1 || sequence < 0) return null;
            var player = _lastBoard.Players[controller];
            var list = location switch
            {
                Location.Hand => player.Hand, Location.MZone => player.Monsters, Location.SZone => player.SpellTraps,
                Location.Grave => player.Graveyard, Location.Removed => player.Banished, Location.Extra => player.ExtraDeck, _ => null,
            };
            return list != null && sequence < list.Count ? list[sequence]?.Code : null;
        }

        /// <summary>Each frame: redraws the guides when the card in focus or the board changes.</summary>
        private void UpdateColumnGuide()
        {
            var card = _lastBoard == null ? null : ColumnCard();
            var key = card == null || _lastBoard == null ? (0u, 0, false) : (card.Code, _boardVersion, CanSummonNow(_lastBoard));
            if (key == _columnsDrawn) return;
            _columnsDrawn = key;
            if (_columnLayer != null) Destroy(_columnLayer.gameObject);
            _columnLayer = null;
            System.Array.Clear(_columnCounts, 0, Columns.Count);
            System.Array.Clear(_columnReady, 0, Columns.Count);
            ColumnLabels.Clear();
            if (card == null || _lastBoard == null) return;

            // Under the cards, over the mat and its empty zones.
            _columnLayer = Ui.Stretched("ColumnGuides", _field, Vector2.zero, Vector2.one);
            _columnLayer.SetSiblingIndex(_cardLayer.GetSiblingIndex());
            var counts = Columns.CardsIn(_lastBoard);
            int needed = Columns.CardsNeeded(card);
            bool summons = Columns.SummonsIntoColumn(card);
            bool now = key.Item3;
            var gold = Ui.Hex("#FFC940");
            for (int c = 0; c < Columns.Count; c++)
            {
                // The field is drawn from player 0's side, so column c is the c-th zone from the left at the bottom.
                bool ready = needed > 0 && counts[c] >= needed && (!summons || Columns.MonsterZoneFree(_lastBoard, 0, c));
                _columnCounts[c] = counts[c];
                _columnReady[c] = ready;
                float x = (c - 2) * Column;
                var stripe = Ui.Placed("Column", _columnLayer, new Vector2(x, 0), new Vector2(Column - 14, 4 * Row + CardView.FieldSize.y + 10));
                var fill = Ui.Image(stripe, ready ? new Color(gold.r, gold.g, gold.b, 0.15f) : new Color(1, 1, 1, 0.05f));
                fill.raycastTarget = false;
                var edge = stripe.gameObject.AddComponent<Outline>();
                edge.effectColor = ready ? new Color(gold.r, gold.g, gold.b, 0.85f) : new Color(1, 1, 1, 0.18f);
                edge.effectDistance = new Vector2(1.5f, -1.5f);

                // Its count, just above the opponent's Spell & Trap row.
                // "summon here" only while you could (your Main Phase); a check mark when the column counts but you can't now.
                bool here = ready && summons && now;
                string label = here ? $"{counts[c]} · summon here" : ready ? $"{counts[c]} ✓" : $"{counts[c]}";
                ColumnLabels.Add(label);
                var badge = Ui.Placed("Count", _columnLayer, new Vector2(x, 2 * Row + CardView.FieldSize.y / 2 + 14), new Vector2(here ? Column - 10 : ready ? 60 : 44, 24));
                Ui.Image(badge, ready ? Ui.Hex("#5A4512") : new Color(0.06f, 0.08f, 0.12f, 0.9f));
                var text = Ui.Text(badge, label, 14, ready ? Ui.Hex("#FFD86A") : Color.white);
                text.fontStyle = FontStyle.Bold;
                var tip = badge.gameObject.AddComponent<TipOnHover>();
                tip.Title = $"Column {c + 1}: {counts[c]} card{(counts[c] == 1 ? "" : "s")}";
                tip.Body = "A column is a Main Monster Zone and a Spell & Trap Zone of each player, facing each other, and the Extra Monster Zone between them (2nd and 4th columns). "
                    + (needed > 0 ? $"{card.Name} needs {needed} or more cards in a column{(summons ? ", and your Main Monster Zone there free to Special Summon it" : "")}." : $"{card.Name} cares about columns.")
                    + (ready && summons && !now ? " You can Summon it in your Main Phase." : "");
            }
        }
    }
}
