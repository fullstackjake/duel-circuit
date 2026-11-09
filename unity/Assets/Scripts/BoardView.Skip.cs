using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The board's side of "Skip to my turn": the button under the status line during the opponent's turn (S, or the
    /// controller's Y), the status while it runs, and the recap of what they did once your turn comes.
    /// </summary>
    public sealed partial class BoardView
    {
        /// <summary>Skip to my turn was asked for (the button, S, or Y on the opponent's turn).</summary>
        public event Action? SkipTurnClicked;
        /// <summary>Whether the opponent's turn can be skipped right now.</summary>
        public Func<bool>? CanSkipTurn;
        /// <summary>A skip is running: the status line says so and the button is gone.</summary>
        public bool Skipping { get; set; }

        private RectTransform? _skipButton, _skipRecap;
        private float _recapShownAt;

        /// <summary>For checks: whether the Skip to my turn button is up.</summary>
        public bool SkipOffered => _skipButton != null;

        /// <summary>For checks: the recap's lines while it's up (null when it isn't).</summary>
        public List<string>? SkipRecap { get; private set; }

        /// <summary>Asks to skip, as the button does (S and the controller's Y come here too).</summary>
        public void RequestSkip()
        {
            if (CanSkipTurn?.Invoke() == true) SkipTurnClicked?.Invoke();
        }

        /// <summary>Each frame: the button shows only while a skip is possible.</summary>
        private void UpdateSkip()
        {
            bool show = _humanMode && !Skipping && CanSkipTurn?.Invoke() == true;
            if (show && _skipButton == null)
            {
                _skipButton = Ui.Placed("SkipTurn", _field, new Vector2(Column * 4 + 6, -2 * Row - 84), new Vector2(150, 36));
                var image = Ui.Image(_skipButton, new Color(0.12f, 0.2f, 0.34f, 0.95f));
                var edge = _skipButton.gameObject.AddComponent<Outline>();
                edge.effectColor = new Color(0.5f, 0.75f, 1f, 0.7f);
                edge.effectDistance = new Vector2(1, -1);
                Ui.Text(_skipButton, "Skip to my turn  »", 14, Color.white).raycastTarget = false;
                image.raycastTarget = true;
                Ui.Clickable(_skipButton, () => { Sounds.Play("click"); RequestSkip(); });
                var tip = _skipButton.gameObject.AddComponent<TipOnHover>();
                tip.Title = "Skip to my turn (S)";
                tip.Body = "Plays the rest of the opponent's turn at once, without animations. It stops for any decision of yours (a chance to chain), and when your turn comes it lists what they did.";
            }
            else if (!show && _skipButton != null)
            {
                Destroy(_skipButton.gameObject);
                _skipButton = null;
            }
            // The recap goes once you've had a look and play on (a click, or the next turn after it).
            if (_skipRecap != null && Time.unscaledTime - _recapShownAt > 25) CloseSkipRecap();
        }

        /// <summary>The recap of a skipped turn: a panel at the top of the board, closed with a click.</summary>
        public void ShowSkipRecap(string title, List<string> lines)
        {
            CloseSkipRecap();
            SkipRecap = lines;
            _recapShownAt = Time.unscaledTime;
            const int MaxLines = 8;
            var shown = lines.Count > MaxLines ? lines.Skip(lines.Count - MaxLines).ToList() : lines;
            float height = 66 + 24 * Math.Max(1, shown.Count) + (lines.Count > MaxLines ? 22 : 0);
            var panel = Ui.Placed("SkipRecap", _field, new Vector2(-150, 470 - height / 2), new Vector2(780, height));
            _skipRecap = panel;
            Ui.Image(panel, new Color(0.04f, 0.07f, 0.12f, 0.95f));
            var edge = panel.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.5f, 0.75f, 1f, 0.6f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
            var head = Ui.Text(Ui.Placed("Title", panel, new Vector2(-20, height / 2 - 22), new Vector2(700, 28)), title, 17, Ui.Hex("#8CC8FF"), TextAnchor.MiddleLeft);
            head.fontStyle = FontStyle.Bold;
            float y = height / 2 - 50;
            if (lines.Count > MaxLines)
            {
                Ui.Text(Ui.Placed("More", panel, new Vector2(-20, y), new Vector2(700, 20)), $"({lines.Count - MaxLines} earlier: see History)", 13,
                    new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft);
                y -= 22;
            }
            foreach (var line in shown)
            {
                Ui.Text(Ui.Placed("Line", panel, new Vector2(-10, y), new Vector2(720, 22)), "•  " + line, 15, new Color(0.9f, 0.92f, 0.96f), TextAnchor.MiddleLeft)
                    .raycastTarget = false;
                y -= 24;
            }
            Ui.Text(Ui.Placed("Close", panel, new Vector2(350, height / 2 - 22), new Vector2(60, 24)), "✕", 18, new Color(1, 1, 1, 0.7f)).raycastTarget = false;
            Ui.Clickable(panel, CloseSkipRecap);
            panel.SetAsLastSibling();
        }

        public void CloseSkipRecap()
        {
            if (_skipRecap != null) Destroy(_skipRecap.gameObject);
            _skipRecap = null;
            SkipRecap = null;
        }
    }
}
