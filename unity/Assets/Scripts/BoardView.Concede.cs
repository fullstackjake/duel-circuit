using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Conceding: click your own Deck (when nothing asks you to pick from it) and the board asks "Concede the duel?".
    /// Conceding ends the duel at once as a loss, as if your Life Points had run out.
    /// </summary>
    public sealed partial class BoardView
    {
        private RectTransform? _concedeDialog;

        /// <summary>You conceded (the controller ends the duel as your loss).</summary>
        public Action? Concede;
        /// <summary>Whether you may concede now (a duel you're playing, not a lesson).</summary>
        public Func<bool>? CanConcede;

        private void DeckClicked(int owner)
        {
            var set = Prompt.Current;
            bool picksDeck = _actionablePiles.Contains((owner, Location.Deck))
                || set?.Choices.Any(c => c.Card is { } r && r.Controller == owner && (r.Location & 0x7f) == Location.Deck) == true;
            if (picksDeck) { CardClicked(owner, Location.Deck, -1); return; }
            if (owner == HumanSeat) OpenConcede();
        }

        /// <summary>The "Concede the duel?" dialog, when you're playing a duel that isn't over.</summary>
        public void OpenConcede()
        {
            if (!_humanMode || Concede == null || CanConcede?.Invoke() != true || _lastBoard == null || _lastBoard.Winner >= 0) return;
            CloseOverlays();
            var root = Ui.Stretched("ConcedeDialog", _field, Vector2.zero, Vector2.one);
            _concedeDialog = root;
            var dim = Ui.Image(root, new Color(0, 0, 0, 0.55f));
            Ui.Clickable((RectTransform)dim.transform, CloseConcede);
            var panel = Ui.Placed("Panel", root, new Vector2(0, 0), new Vector2(660, 260));
            Ui.Image(panel, new Color(0.08f, 0.06f, 0.08f, 0.97f));
            var edge = panel.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(1f, 0.45f, 0.4f, 0.6f);
            edge.effectDistance = new Vector2(2, -2);
            Ui.Text(Ui.Placed("Title", panel, new Vector2(0, 78), new Vector2(600, 44)), "Concede the duel?", 30, Color.white).fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Placed("Body", panel, new Vector2(0, 26), new Vector2(600, 30)), "It ends now and counts as a loss.", 18,
                new Color(1, 1, 1, 0.75f)).raycastTarget = false;
            var concede = Ui.Button(panel, "Concede", new Vector2(-130, -66), new Vector2(220, 54), ConfirmConcede);
            concede.GetComponent<Image>().color = Ui.Hex("#B23A32");
            Ui.Button(panel, "Keep playing", new Vector2(130, -66), new Vector2(220, 54), CloseConcede);
            root.SetAsLastSibling();
        }

        public void CloseConcede()
        {
            if (_concedeDialog != null) Destroy(_concedeDialog.gameObject);
            _concedeDialog = null;
        }

        private void ConfirmConcede()
        {
            CloseConcede();
            Concede?.Invoke();
        }

        /// <summary>For checks: whether the dialog is open; clicking your deck; and its Concede button.</summary>
        public bool ConcedeOpen => _concedeDialog != null;
        public void DebugDeckClick() => DeckClicked(HumanSeat);
        public void DebugConfirmConcede() { if (_concedeDialog != null) ConfirmConcede(); }
    }
}
