using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Changing phase (Master Duel's "Select Phase to Change to"): the turn disc opens a bar of the six phases, the ones
    /// you can go to lit up and the current one marked. One click goes there: Main Phase 2 from Main Phase 1 passes
    /// through the Battle Phase for you, and End from Main Phase 1 skips it. B and E (or the controller's Y, which opens
    /// the bar) work too.
    /// </summary>
    public sealed partial class BoardView
    {
        private const string ToBattle = "Go to the Battle Phase", ToMain2 = "Go to Main Phase 2", ToEnd = "End your turn";
        private RectTransform? _phasePicker;
        /// <summary>Main Phase 2 asked for from Main Phase 1: taken as soon as the Battle Phase lets you.</summary>
        private bool _pendingMain2;
        private int _pendingTurn;

        /// <summary>The phases you can go to now, from the current decision.</summary>
        private (bool battle, bool main2, bool end) Reachable()
        {
            var set = Prompt.Current;
            if (set == null || !(set.Prompt is IdlePrompt || set.Prompt is BattlePrompt)) return (false, false, false);
            bool Has(string label) => set.Choices.Any(c => c.Card == null && c.Label == label);
            bool battle = Has(ToBattle), main2 = Has(ToMain2), end = Has(ToEnd);
            // From Main Phase 1, Main Phase 2 is through the Battle Phase.
            return (battle, main2 || battle, end);
        }

        /// <summary>The turn disc was clicked (or Y pressed): on your turn, the phase bar.</summary>
        public void OpenPhasePicker()
        {
            var (battle, main2, end) = Reachable();
            if (!battle && !main2 && !end) return;
            ClosePhasePicker();
            CloseActionMenu();
            var root = Ui.Stretched("PhasePicker", _field, Vector2.zero, Vector2.one);
            _phasePicker = root;
            var dim = Ui.Image(root, new Color(0, 0, 0, 0.45f));
            Ui.Clickable((RectTransform)dim.transform, ClosePhasePicker);
            var panel = Ui.Placed("Panel", root, new Vector2(0, -60), new Vector2(1240, 330));
            Ui.Image(panel, new Color(0.05f, 0.09f, 0.16f, 0.96f));
            var edge = panel.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.45f, 0.7f, 1f, 0.5f);
            edge.effectDistance = new Vector2(2, -2);
            Ui.Text(Ui.Placed("Title", panel, new Vector2(0, 130), new Vector2(1000, 40)), "Change phase", 26, Color.white).fontStyle = FontStyle.Bold;

            ushort now = _lastBoard?.Phase ?? 0;
            int current = now switch
            {
                Phase.Draw => 0, Phase.Standby => 1, Phase.Main1 => 2, Phase.Main2 => 4, Phase.End => 5,
                _ => (now & (Phase.BattleStart | Phase.BattleStep | Phase.Damage | Phase.DamageCal | Phase.Battle)) != 0 ? 3 : 2,
            };
            var phases = new (string label, bool reachable, Action go)[]
            {
                ("Draw", false, () => { }), ("Standby", false, () => { }), ("Main 1", false, () => { }),
                ("Battle", battle && current < 3, () => GoToPhase("battle")),
                ("Main 2", main2 && current < 4, () => GoToPhase("main2")),
                ("End", end, () => GoToPhase("end")),
            };
            for (int i = 0; i < phases.Length; i++)
            {
                var (label, reachable, go) = phases[i];
                float x = -500 + i * 200;
                if (i > 0) Ui.Image(Ui.Placed("Link", panel, new Vector2(x - 100, 10), new Vector2(84, 3)), new Color(1, 1, 1, 0.18f)).raycastTarget = false;
                var disc = Icons.Disc(panel, new Vector2(x, 10), label, () => { if (reachable) go(); }, size: 112, primary: reachable);
                var group = disc.gameObject.AddComponent<CanvasGroup>();
                group.alpha = reachable || i == current ? 1f : 0.32f;
                group.interactable = group.blocksRaycasts = reachable;
                if (i == current)
                    Ui.Text(Ui.Placed("Here", panel, new Vector2(x, 92), new Vector2(60, 30)), "▼", 24, Color.white).raycastTarget = false;
            }
            Ui.Text(Ui.Placed("Keys", panel, new Vector2(0, -98), new Vector2(1000, 24)), "B: Battle Phase  ·  M: Main Phase 2  ·  E: end your turn  ·  Esc: cancel", 14,
                new Color(1, 1, 1, 0.5f)).raycastTarget = false;
            Ui.Button(panel, "Cancel", new Vector2(0, -136), new Vector2(240, 44), ClosePhasePicker);
            root.SetAsLastSibling();
        }

        public void ClosePhasePicker()
        {
            if (_phasePicker != null) Destroy(_phasePicker.gameObject);
            _phasePicker = null;
        }

        /// <summary>Whether the phase bar is open (for checks).</summary>
        public bool PhasePickerOpen => _phasePicker != null;

        /// <summary>The lit phases in the open bar (for checks).</summary>
        public List<string> DebugPhasePicker() => _phasePicker == null ? new List<string>()
            : _phasePicker.GetComponentsInChildren<CanvasGroup>().Where(g => g.interactable && g.transform != _phasePicker).Select(g => g.GetComponentInChildren<Text>()?.text ?? "").ToList();

        /// <summary>
        /// Goes to a phase ("battle", "main2", "end") if you can from here. "main2" from Main Phase 1 goes through the
        /// Battle Phase; "main2-direct" (the M key) only moves when Main Phase 2 is offered right now, so a stray key
        /// never starts a Battle Phase.
        /// </summary>
        public bool GoToPhase(string phase)
        {
            var set = Prompt.Current;
            if (set == null) return false;
            int Choice(string label) => set.Choices.FindIndex(c => c.Card == null && c.Label == label);
            int pick = -1;
            switch (phase)
            {
                case "battle": pick = Choice(ToBattle); break;
                case "end": pick = Choice(ToEnd); break;
                case "main2":
                    pick = Choice(ToMain2);
                    if (pick < 0 && (pick = Choice(ToBattle)) >= 0) { _pendingMain2 = true; _pendingTurn = _lastBoard?.Turn ?? 0; }
                    break;
                case "main2-direct": pick = Choice(ToMain2); break;
            }
            if (pick < 0) return false;
            ClosePhasePicker();
            CloseActionMenu();
            Prompt.ClickChoice(pick);
            return true;
        }

        /// <summary>Called each frame: a Main Phase 2 asked for from Main Phase 1 is taken once the Battle Phase asks.</summary>
        private void UpdatePendingPhase()
        {
            if (!_pendingMain2) return;
            if (_lastBoard != null && _lastBoard.Turn != _pendingTurn) { _pendingMain2 = false; return; }
            var set = Prompt.Current;
            if (set?.Prompt is BattlePrompt)
            {
                int pick = set.Choices.FindIndex(c => c.Card == null && c.Label == ToMain2);
                _pendingMain2 = false;
                if (pick >= 0) Prompt.ClickChoice(pick);
            }
            else if (set?.Prompt is IdlePrompt && _lastBoard?.Phase == Phase.Main2) _pendingMain2 = false;
        }
    }
}
