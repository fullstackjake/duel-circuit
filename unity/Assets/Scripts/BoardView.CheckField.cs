using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Check Field (Link Evolution's X button): whatever covers the board — a question, a picker, a menu — fades away so
    /// you can look at the field, then comes back. Hold Space (or the controller's Check Field button, RT) to peek; the
    /// ▼ on a dialog keeps it hidden until you click the pill at the top, press Esc, or the next decision comes.
    /// </summary>
    public sealed partial class BoardView
    {
        private bool _checkHeld, _checkSticky;
        private ChoiceSet? _checkSet;
        private RectTransform? _checkPill;
        private bool _pillSticky;

        /// <summary>Whether the board is being checked (the overlays hidden).</summary>
        public bool CheckingField => _checkHeld || _checkSticky;

        /// <summary>Checks the field until you come back (the dialogs' ▼); again comes back.</summary>
        public void ToggleCheckField()
        {
            _checkSticky = !_checkSticky;
            _checkSet = Prompt.Current;
            ApplyCheckField();
        }

        /// <summary>For checks: holds (or lets go of) the Check Field key.</summary>
        public void DebugHoldCheckField(bool held)
        {
            _debugHeld = held;
            UpdateCheckField();
        }

        private bool _debugHeld;

        /// <summary>Whether everything over the board is hidden right now (for checks): each overlay showing is invisible.</summary>
        public bool OverlaysHidden
        {
            get
            {
                foreach (var overlay in Overlays())
                {
                    if (overlay == null) continue;
                    var group = overlay.GetComponent<CanvasGroup>();
                    if (group == null || group.alpha > 0) return false;
                }
                return true;
            }
        }

        private RectTransform?[] Overlays() => new[] { Dialogs.Root, _pileViewer, _actionMenu, _phasePicker, _concedeDialog, _skipRecap };

        /// <summary>Each frame: the held key, a new decision ending a sticky check, and the overlays' visibility.</summary>
        private void UpdateCheckField()
        {
            bool held = _debugHeld || ControllerSupport.IsHeld(PadAction.CheckField);
#if ENABLE_INPUT_SYSTEM
            var keys = UnityEngine.InputSystem.Keyboard.current;
            if (keys != null && keys.spaceKey.isPressed && !TypingSomewhere() && KeysElsewhere?.Invoke() != true)
            {
                held = true;
                // Space mustn't also press the button last clicked.
                if (keys.spaceKey.wasPressedThisFrame) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            }
#else
            if (Input.GetKey(KeyCode.Space)) held = true;
#endif
            _checkHeld = held;
            // A new decision: show it (you'd want to see what's being asked).
            if (_checkSticky && Prompt.Current != _checkSet) _checkSticky = false;
            ApplyCheckField();
        }

        private void ApplyCheckField()
        {
            bool on = CheckingField;
            bool any = false;
            foreach (var overlay in Overlays())
            {
                if (overlay == null) continue;
                any = true;
                var group = overlay.GetComponent<CanvasGroup>();
                if (group == null) group = overlay.gameObject.AddComponent<CanvasGroup>();
                group.alpha = on ? 0 : 1;
                group.blocksRaycasts = !on;
            }
            bool pill = on && any;
            if (pill == (_checkPill != null) && (!pill || _pillSticky == _checkSticky)) return;
            if (_checkPill != null) Destroy(_checkPill.gameObject);
            _checkPill = null;
            if (!pill) return;
            _pillSticky = _checkSticky;
            // A pill at the top: what's going on, and (when it stays) how to come back.
            _checkPill = Ui.Placed("CheckField", _field, new Vector2(0, 476), new Vector2(560, 50));
            Ui.Image(_checkPill, new Color(0.08f, 0.25f, 0.5f, 0.95f));
            var edge = _checkPill.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.5f, 0.78f, 1f, 0.8f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);
            var text = Ui.Text(_checkPill, _checkSticky ? "Checking the field   <b>▲ Back</b>" : "Checking the field  ·  let go of Space to come back", 18, Color.white);
            text.supportRichText = true;
            if (_checkSticky) Ui.Clickable(_checkPill, ToggleCheckField);
            _checkPill.SetAsLastSibling();
        }
    }
}
