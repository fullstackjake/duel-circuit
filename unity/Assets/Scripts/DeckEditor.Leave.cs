using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Leaving with unsaved changes (docs/dev/EDITOR-QUICK-FIXES-SPEC-2026-11-20.md part A): Back, New, a deck picked in
    /// Decks..., the controller's B and Esc ask "Save changes to X?" (Save, Don't save, Cancel) while the deck has unsaved
    /// changes, and act at once otherwise. A dot on the Save button shows those changes. <see cref="Close"/> stays immediate:
    /// the campaign, the checks and the duel call it.
    /// </summary>
    public sealed partial class DeckEditor
    {
        private RectTransform? _leaveDialog;
        private Text? _leaveTitle;
        /// <summary>What waits for the answer: leave, a new deck, another deck.</summary>
        private Action? _leaveAction;
        private GameObject _unsavedDot = null!;

        /// <summary>A 10 px dot in the Warn colour inside the Save button's top-right corner; it never takes the pointer.</summary>
        private void BuildUnsavedMark(Button save)
        {
            var T = UiTheme.Current;
            var dot = Ui.Rect("UnsavedDot", save.transform);
            dot.anchorMin = dot.anchorMax = dot.pivot = Vector2.one;
            dot.anchoredPosition = new Vector2(-4, -4);
            dot.sizeDelta = new Vector2(10, 10);
            var image = dot.gameObject.AddComponent<RawImage>();
            image.texture = Icons.Round;
            image.color = T.Warn;
            image.raycastTarget = false;
            _unsavedDot = dot.gameObject;
            _unsavedDot.SetActive(false);
        }

        /// <summary>Each frame: the dot shows exactly while the deck has unsaved changes.</summary>
        private void ShowUnsavedMark()
        {
            if (_unsavedDot != null && _unsavedDot.activeSelf != _dirty) _unsavedDot.SetActive(_dirty);
        }

        /// <summary>Runs <paramref name="action"/> now, or, with unsaved changes, once the leave question is answered.</summary>
        private void AskFirst(Action action)
        {
            if (!_dirty) { action(); return; }
            OpenLeaveDialog(action);
        }

        /// <summary>A deck picked in Decks...: it opens (asking first with unsaved changes; Cancel leaves the list open).</summary>
        private void PickFromList(string name) => AskFirst(() => { CloseBrowser(); LoadDeck(name); });

        /// <summary>
        /// The question, over everything in the editor: a scrim (a click on it is Cancel) and a 560 x 220 panel, centred.
        /// Enter is Save, Esc and B are Cancel (DeckEditor.Keys); the focus keys, the shortcuts, the wheel and the change
        /// message are off under it.
        /// </summary>
        private void OpenLeaveDialog(Action action)
        {
            var T = UiTheme.Current;
            CloseLeaveDialog();
            _leaveAction = action;
            var root = Ui.Stretched("LeaveDialog", _root, Vector2.zero, Vector2.one);
            _leaveDialog = root;
            Ui.Clickable((RectTransform)Ui.Image(root, T.Scrim.WithAlpha(0.7f)).transform, () => LeaveAnswer("cancel"));
            var panel = Box(root, (1920 - 560) / 2f, (1080 - 220) / 2f, 560, 220);
            Ui.Image(panel, T.PanelDeep); // (it takes the pointer: a click on the panel isn't one on the scrim)
            var name = string.IsNullOrWhiteSpace(_deck.Name) ? "new deck" : _deck.Name.Trim();
            _leaveTitle = Label(28, 26, 504, 34, $"Save changes to {name}?", 22, TextAnchor.MiddleLeft, panel);
            _leaveTitle.fontStyle = FontStyle.Bold;
            _leaveTitle.color = T.Text;
            _leaveTitle.resizeTextForBestFit = true; // a long deck name shrinks rather than runs off
            _leaveTitle.resizeTextMinSize = 14;
            _leaveTitle.resizeTextMaxSize = 22;
            Label(28, 70, 504, 50, "You changed this deck and haven't saved it.", 15, TextAnchor.UpperLeft, panel).color = T.TextSoft;
            // Right-aligned: Save (the accent), Don't save, Cancel.
            const float y = 220 - 28 - 40;
            Button(560 - 28 - 110, y, 110, 40, "Cancel", () => LeaveAnswer("cancel"), panel);
            Button(560 - 28 - 110 - 10 - 130, y, 130, 40, "Don't save", () => LeaveAnswer("discard"), panel);
            var save = Button(560 - 28 - 110 - 10 - 130 - 10 - 110, y, 110, 40, "Save", () => LeaveAnswer("save"), panel);
            save.targetGraphic.color = T.Primary;
            save.GetComponentInChildren<Text>().color = T.OnAccent;
            root.SetAsLastSibling();
        }

        private void CloseLeaveDialog()
        {
            if (_leaveDialog != null) Destroy(_leaveDialog.gameObject);
            _leaveDialog = null;
            _leaveTitle = null;
            _leaveAction = null;
        }

        /// <summary>
        /// The answer. Save: <see cref="Save"/>; saved (or nothing to save), the waiting action runs; when Save opens its own
        /// "how to save" bar, the action is dropped and the status says to choose first. Don't save: the recovery copy goes,
        /// then the action runs. Cancel: nothing changes.
        /// </summary>
        private void LeaveAnswer(string answer)
        {
            if (_leaveDialog == null) return;
            var action = _leaveAction;
            CloseLeaveDialog();
            switch (answer)
            {
                case "save":
                    Save();
                    if (!_dirty) action?.Invoke();
                    else if (_savePrompt != null) SetStatus("Choose how to save, then leave.");
                    break;
                case "discard":
                    ClearRecovery();
                    action?.Invoke();
                    break;
                default:
                    break; // Cancel: the deck, its mark and the status stay as they were
            }
        }

        // ---- For checks ---------------------------------------------------------------------------------------------------

        /// <summary>For checks: the Save button's dot is showing.</summary>
        public bool DebugUnsavedMark => _unsavedDot != null && _unsavedDot.activeSelf;
        public bool DebugLeaveDialogOpen => _leaveDialog != null;
        /// <summary>For checks: the question's title ("" when shut).</summary>
        public string DebugLeaveDialogTitle => _leaveTitle != null ? _leaveTitle.text : "";
        /// <summary>For checks: answers the question: "save", "discard" or "cancel".</summary>
        public void DebugLeaveAnswer(string answer) => LeaveAnswer(answer);
        /// <summary>For checks: the Back button.</summary>
        public void DebugBack() => AskFirst(Close);
        /// <summary>For checks: a deck picked in Decks... (the list opens first, as the player sees it).</summary>
        public void DebugOpenFromList(string name)
        {
            ShowBrowser();
            PickFromList(name);
        }
        /// <summary>For checks: the deck list's Close.</summary>
        public void DebugCloseBrowser() => CloseBrowser();
        /// <summary>For checks: the deck list (Decks...) is open.</summary>
        public bool DebugBrowserOpen => _browser != null;
        /// <summary>For checks: whether the deck has unsaved changes.</summary>
        public bool DebugDirty => _dirty;

        /// <summary>For checks: the centre on screen of an active button with this label (the leave question's first), or null.</summary>
        public Vector2? DebugButtonOnScreen(string label)
        {
            var within = _leaveDialog != null ? (Transform)_leaveDialog : _root;
            var button = within.GetComponentsInChildren<Button>().FirstOrDefault(b => b.isActiveAndEnabled && b.GetComponentInChildren<Text>()?.text == label);
            return button == null ? (Vector2?)null : OnScreen(button.transform);
        }
    }
}
