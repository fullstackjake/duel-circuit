using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The editor's shortcuts (docs/dev/EDITOR-QUICK-FIXES-SPEC-2026-11-20.md part D). Ctrl and Cmd both work on both systems.
    /// <list type="table">
    /// <item>Ctrl/Cmd+S: Save, also while a text field has the keyboard.</item>
    /// <item>Ctrl/Cmd+Z: Undo; Ctrl/Cmd+Y or Ctrl/Cmd+Shift+Z: Redo. Not while typing (the field keeps the key).</item>
    /// <item>Ctrl/Cmd+F (also while typing) or "/" (a slash while typing): the search box takes the keyboard, its text selected.</item>
    /// <item>Esc: closes the top thing (the leave question, Save's bar, the reading view, the deck list, the test hand, the More
    /// panel), and with none open is Back (asking first with unsaved changes). While typing, Esc only leaves the field (Unity's
    /// own Esc): with the one-frame grace after typing, it never also closes or leaves in the same press.</item>
    /// </list>
    /// All are off mid-drag and while the leave question is open, where Enter is Save and Esc is Cancel. Every enabled keyboard
    /// counts, as for the focus keys (the checks plug in their own).
    /// </summary>
    public sealed partial class DeckEditor
    {
        /// <summary>The search box takes the keyboard next frame ("/" isn't typed into it); its text before, to undo a slash that got in.</summary>
        private bool _searchNextFrame;
        private (string before, int frames)? _slashGuard;

        private void Shortcuts()
        {
#if ENABLE_INPUT_SYSTEM
            bool esc = Down(UnityEngine.InputSystem.Key.Escape);
            bool enter = Down(UnityEngine.InputSystem.Key.Enter) || Down(UnityEngine.InputSystem.Key.NumpadEnter);
            bool s = Down(UnityEngine.InputSystem.Key.S), z = Down(UnityEngine.InputSystem.Key.Z), y = Down(UnityEngine.InputSystem.Key.Y);
            bool f = Down(UnityEngine.InputSystem.Key.F), slash = Down(UnityEngine.InputSystem.Key.Slash);
#else
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            bool s = Input.GetKeyDown(KeyCode.S), z = Input.GetKeyDown(KeyCode.Z), y = Input.GetKeyDown(KeyCode.Y);
            bool f = Input.GetKeyDown(KeyCode.F), slash = Input.GetKeyDown(KeyCode.Slash);
#endif
            bool typing = Typing();
            // Esc or Enter that ends a text field this frame (or ended it last frame) is the field's alone.
            bool grace = typing || _typingLastFrame;
            GuardSlash();
            if (_searchNextFrame)
            {
                _searchNextFrame = false;
                TakeSearchKeyboard();
            }

            if (_leaveDialog != null)
            {
                // The question has the keys this frame, answered or not: nothing under it (the focus keys) sees them, even once
                // the answer has shut it (Enter that saves and opens the next deck must not also add a card to it).
                _keysTakenFrame = Time.frameCount;
                if (grace) return;
                if (esc) LeaveAnswer("cancel");
                else if (enter) LeaveAnswer("save");
                return;
            }
            if (_dragging) return;
            bool command = CommandHeld();
            // Overlays over the deck: the reading view, the deck list and Save's bar take Ctrl+S and Ctrl+F too (Save would re-ask
            // and wipe a typed name; the search box is out of sight); Undo / Redo also wait while the test hand covers the deck.
            // (Under the More panel all four work.)
            bool overlay = _reading != null || _browser != null || _savePrompt != null;
            bool covered = overlay || _handOverlay.gameObject.activeSelf;
            if (command && s) { if (!overlay) Save(); Take(); return; }
            if (command && f) { if (!overlay) AskSearchKeyboard(); Take(); return; }
            if (command && !typing && (y || (z && ShiftHeld()))) { if (!covered) Redo(); Take(); return; }
            if (command && !typing && z) { if (!covered) Undo(); Take(); return; }
            if (slash && !command && !grace && !overlay) { AskSearchKeyboard(); Take(); return; }
            if (esc && !grace) { BackOne(); Take(); }
        }

        /// <summary>The frame a shortcut (or the leave question) used the keys: the focus keys skip it (DeckEditor.Focus).</summary>
        private int _keysTakenFrame = -1;
        private void Take() => _keysTakenFrame = Time.frameCount;

        /// <summary>Ctrl (either side) or Cmd held on any enabled keyboard.</summary>
        private static bool CommandHeld()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
                if (device is UnityEngine.InputSystem.Keyboard k && k.enabled
                    && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed || k.leftMetaKey.isPressed || k.rightMetaKey.isPressed)) return true;
            return false;
#else
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
#endif
        }

        /// <summary>
        /// Esc and the controller's B: the top thing closes (the leave question, Save's bar, the reading view, the deck list,
        /// the test hand, the More panel); with none open, Back (asking first with unsaved changes).
        /// </summary>
        private void BackOne()
        {
            if (_leaveDialog != null) LeaveAnswer("cancel");
            else if (_savePrompt != null) CloseSavePrompt();
            else if (_reading != null) CloseReading();
            else if (_browser != null) CloseBrowser();
            else if (_handOverlay.gameObject.activeSelf) CloseTestHand();
            else if (MoreOpen) CloseMore();
            else AskFirst(Close);
        }

        /// <summary>The search box takes the keyboard next frame, so the key that asked ("/") isn't typed into it.</summary>
        private void AskSearchKeyboard() => _searchNextFrame = true;

        /// <summary>The search box takes the keyboard with its text selected (typing replaces it).</summary>
        private void TakeSearchKeyboard()
        {
            if (!IsOpen || _searchField == null) return;
            _slashGuard = (_searchField.text, 3);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_searchField.gameObject);
            _searchField.ActivateInputField(); // selects all on focus (the field's default)
        }

        /// <summary>
        /// A "/" that still reached the search box as it took the keyboard (a real keyboard's text event can come a frame
        /// late) is taken out again, its text selected as asked.
        /// </summary>
        private void GuardSlash()
        {
            if (_slashGuard is not { } guard) return;
            if (guard.frames <= 0) { _slashGuard = null; return; }
            _slashGuard = (guard.before, guard.frames - 1);
            var text = _searchField.text;
            if (text == guard.before || guard.before == "/") return;
            if (text == "/" || text == guard.before + "/")
            {
                _searchField.text = guard.before; // (runs the search again, as typing does)
                _searchField.selectionAnchorPosition = 0;
                _searchField.selectionFocusPosition = guard.before.Length;
                _slashGuard = null;
            }
        }

        // ---- For checks ---------------------------------------------------------------------------------------------------

        /// <summary>For checks: the search box has the keyboard.</summary>
        public bool DebugSearchHasKeyboard => _searchField != null && _searchField.isFocused;

        /// <summary>For checks: the detail shows an ownership line for the selected card.</summary>
        public bool DebugOwnershipLine => _cardDetail.OwnershipShown;

        /// <summary>For checks: the editor's ban lists, in their order (No limits first).</summary>
        public IReadOnlyList<Banlist> DebugBanlists => _banlists;

        /// <summary>For checks: the card on the left is a favorite.</summary>
        public bool DebugSelectedFavorite => _selected != null && _favorites.Contains(Key(_selected.Code));

        /// <summary>For checks: the help lines under the results.</summary>
        public string DebugHelp => _help != null ? _help.text : "";
    }
}
