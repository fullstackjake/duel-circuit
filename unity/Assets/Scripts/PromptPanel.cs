using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Shows the decision the human player owes the engine: a title, up to six choices per page,
    /// and Confirm / Finish / Cancel when the prompt allows them. Produces the core's response bytes.
    /// </summary>
    public sealed class PromptPanel
    {
        private const int PerPage = 6;
        private const float ButtonHeight = 30f;

        private static readonly Color Idle = Ui.Hex("#2A3140");
        private static readonly Color Toggled = Ui.Hex("#3D6FB5");
        private static readonly Color Picked = Ui.Hex("#3B4A33");

        private readonly RectTransform _root;
        private readonly Text _title;
        private readonly Text _detail;
        private readonly Text _status;
        private readonly Text _pageLabel;
        private readonly List<(Button button, Image image, Text label)> _slots = new List<(Button, Image, Text)>();
        private readonly Button _prev, _next, _confirm, _finish, _cancel, _showAll;
        // Turn actions ("Go to the Battle Phase", "End your turn") are pinned to the bottom row so they never page away.
        private readonly List<(Button button, Text label)> _pinned = new List<(Button, Text)>();
        private List<int> _pinnedChoices = new List<int>();

        private ChoiceSet? _set;
        private readonly HashSet<int> _toggled = new HashSet<int>();
        /// <summary>Order mode: the choices clicked so far, first to last.</summary>
        private readonly List<int> _ordered = new List<int>();

        /// <summary>Order mode: where a choice is in the order so far (0 first), or -1.</summary>
        public int OrderOf(int index) => _ordered.IndexOf(index);

        /// <summary>Order mode: start the order over.</summary>
        public void ResetOrder()
        {
            _ordered.Clear();
            _toggled.Clear();
            Refresh();
        }
        private List<int> _visible = new List<int>();
        private bool _filtered;
        private int _page;

        /// <summary>Raised with the response bytes once the player has decided.</summary>
        public event Action<byte[]>? Answered;

        public ChoiceSet? Current => _set;

        /// <summary>Raised after the panel redraws (a new decision, a toggled pick, a page change).</summary>
        public event Action? Refreshed;

        public PromptPanel(RectTransform root)
        {
            _root = root;
            Ui.Image(root, Ui.Hex("#161A23"));

            _title = Ui.Text(Row("Title", 6, 40), "", 16, Color.white, TextAnchor.UpperLeft);
            _title.fontStyle = FontStyle.Bold;
            _title.supportRichText = true;
            _detail = Ui.Text(Row("Detail", 46, 18), "", 13, new Color(0.78f, 0.8f, 0.86f), TextAnchor.UpperLeft);
            _status = Ui.Text(Row("Status", 64, 18), "", 13, Ui.Hex("#FF7A7A"), TextAnchor.UpperLeft);

            for (int i = 0; i < PerPage; i++)
            {
                var rt = Row($"Choice{i}", 84 + i * (ButtonHeight + 4), ButtonHeight);
                var image = Ui.Image(rt, Idle);
                var button = rt.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                int slot = i;
                button.onClick.AddListener(() => ClickSlot(slot));
                UiHover.Attach(button);
                var label = Ui.Text(rt, "", 14, Color.white, TextAnchor.MiddleLeft);
                label.rectTransform.offsetMin = new Vector2(10, 0);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 10;
                label.resizeTextMaxSize = 14;
                _slots.Add((button, image, label));
            }

            float pagerY = 84 + PerPage * (ButtonHeight + 4) + 2;
            _prev = SmallButton("< Prev", new Vector2(0.02f, 0), new Vector2(0.24f, 0), pagerY, () => { _page--; Refresh(); });
            _pageLabel = Ui.Text(Row("Page", pagerY, 26, 0.26f, 0.5f), "", 13, new Color(1, 1, 1, 0.6f));
            _next = SmallButton("Next >", new Vector2(0.5f, 0), new Vector2(0.72f, 0), pagerY, () => { _page++; Refresh(); });
            _showAll = SmallButton("Show all", new Vector2(0.74f, 0), new Vector2(0.98f, 0), pagerY, () => Filter(null));

            float actionY = pagerY + 32;
            _confirm = SmallButton("Confirm", new Vector2(0.02f, 0), new Vector2(0.34f, 0), actionY, Confirm);
            _finish = SmallButton("Finish", new Vector2(0.36f, 0), new Vector2(0.66f, 0), actionY, () => Answer(_set?.Finish()));
            _cancel = SmallButton("Cancel", new Vector2(0.68f, 0), new Vector2(0.98f, 0), actionY, () => Answer(_set?.Cancel()));
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                var b = SmallButton("", new Vector2(0.02f + i * 0.33f, 0), new Vector2(0.33f + i * 0.33f, 0), actionY, () =>
                {
                    if (slot < _pinnedChoices.Count) ClickChoice(_pinnedChoices[slot]);
                });
                _pinned.Add((b, b.GetComponentInChildren<Text>()));
            }

            Show(null, false);
        }

        public void Show(ChoiceSet? set, bool previousRejected)
        {
            _set = set;
            _toggled.Clear();
            _ordered.Clear();
            _filtered = false;
            _page = 0;
            // In single-choice prompts that mix card actions with card-less turn actions, pin the turn actions.
            bool pin = set != null && set.Mode == ChoiceMode.Single && set.Choices.Any(c => c.Card != null) && !set.CanCancel;
            _pinnedChoices = pin ? Enumerable.Range(0, set!.Choices.Count).Where(i => set.Choices[i].Card == null).Take(3).ToList() : new List<int>();
            _visible = set == null ? new List<int>() : Enumerable.Range(0, set.Choices.Count).Where(i => !_pinnedChoices.Contains(i)).ToList();
            _status.text = set != null && previousRejected ? "That choice isn't allowed. Try again." : "";
            Refresh();
        }

        /// <summary>Limits the list to some choices (e.g. the actions for a clicked card); null shows everything.</summary>
        public void Filter(IReadOnlyList<int>? indices)
        {
            if (_set == null) return;
            _filtered = indices != null;
            _visible = indices?.ToList() ?? Enumerable.Range(0, _set.Choices.Count).Where(i => !_pinnedChoices.Contains(i)).ToList();
            _page = 0;
            Refresh();
        }

        /// <summary>Same as clicking the choice's button.</summary>
        public void ClickChoice(int index)
        {
            if (_set == null || index < 0 || index >= _set.Choices.Count) return;
            switch (_set.Mode)
            {
                case ChoiceMode.Multi:
                    if (!_toggled.Remove(index))
                    {
                        // A single-pick-at-a-time feel: when only one may be chosen, swap instead of refusing.
                        if (_set.Max == 1) _toggled.Clear();
                        if (_toggled.Count < _set.Max) _toggled.Add(index);
                    }
                    Refresh();
                    break;
                case ChoiceMode.Order:
                    // A click adds it next; clicking one already placed takes it (and those after it) back out.
                    int at = _ordered.IndexOf(index);
                    if (at >= 0)
                    {
                        foreach (var later in _ordered.Skip(at)) _toggled.Remove(later);
                        _ordered.RemoveRange(at, _ordered.Count - at);
                    }
                    else
                    {
                        _ordered.Add(index);
                        _toggled.Add(index);
                    }
                    Refresh();
                    break;
                default:
                    ChoiceTaken?.Invoke(_set, index);
                    Answer(_set.Choose(index));
                    break;
            }
        }

        /// <summary>A single choice was taken (before it's answered): which one, from which decision.</summary>
        public event Action<ChoiceSet, int>? ChoiceTaken;

        private void ClickSlot(int slot)
        {
            int i = _page * PerPage + slot;
            if (i >= _visible.Count) return;
            // A toggle clicked again within a moment (a long frame, a bounce) is the same click: it would untick the pick.
            if (_set != null && _set.Mode != ChoiceMode.Single && ClickGuard.Repeat(_set, _visible[i])) return;
            ClickChoice(_visible[i]);
        }

        /// <summary>Controller LB / RB: previous / next page of choices.</summary>
        public void PageBy(int delta)
        {
            if (_set == null) return;
            _page += delta;
            Refresh();
        }

        /// <summary>Controller B: decline (cancel) if the prompt allows it, otherwise finish a selection.</summary>
        public void Back()
        {
            if (_set?.CanCancel == true) CancelSelection();
            else if (_set?.CanFinish == true) FinishSelection();
        }

        public void FinishSelection() => Answer(_set?.CanFinish == true ? _set.Finish() : null);
        public void CancelSelection()
        {
            if (_set?.CanCancel != true) return;
            Cancelled?.Invoke();
            Answer(_set.Cancel());
        }

        /// <summary>Raised when the prompt is cancelled (before the answer goes out).</summary>
        public event Action? Cancelled;
        public bool IsSelected(int index) => _toggled.Contains(index);

        public void Confirm()
        {
            if (_set?.Mode == ChoiceMode.Order)
            {
                if (_ordered.Count == _set.Max) Answer(_set.Choose(_ordered.ToList()));
                return;
            }
            if (_set != null && _set.IsValidCount(_toggled.Count))
                Answer(_set.Choose(_toggled.OrderBy(i => i).ToList()));
        }

        private void Answer(byte[]? response)
        {
            if (response == null) return;
            _set = null;
            Refresh();
            Answered?.Invoke(response);
        }

        private void Refresh()
        {
            var set = _set;
            _root.gameObject.SetActive(true);
            if (set == null)
            {
                _title.text = "Waiting for your opponent...";
                _detail.text = "";
                foreach (var (button, _, _) in _slots) button.gameObject.SetActive(false);
                foreach (var b in new[] { _prev, _next, _confirm, _finish, _cancel, _showAll }) b.gameObject.SetActive(false);
                foreach (var (b, _) in _pinned) b.gameObject.SetActive(false);
                _pageLabel.text = "";
                Refreshed?.Invoke();
                return;
            }

            _title.text = PromptDialogs.Highlight(set.Title);
            var detail = set.Detail;
            if (set.Mode == ChoiceMode.Multi) detail = $"{detail} Selected {_toggled.Count}.".Trim();
            if (_filtered) detail = $"Actions for that card. {detail}".Trim();
            _detail.text = detail;

            int pages = Math.Max(1, (_visible.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            for (int slot = 0; slot < PerPage; slot++)
            {
                int i = _page * PerPage + slot;
                var (button, image, label) = _slots[slot];
                button.gameObject.SetActive(i < _visible.Count);
                if (i >= _visible.Count) continue;
                var choice = set.Choices[_visible[i]];
                label.text = choice.Label;
                image.color = _toggled.Contains(_visible[i]) ? Toggled : choice.AlreadySelected ? Picked : Idle;
            }

            _prev.gameObject.SetActive(pages > 1);
            _next.gameObject.SetActive(pages > 1);
            _prev.interactable = _page > 0;
            _next.interactable = _page < pages - 1;
            _pageLabel.text = pages > 1 ? $"{_page + 1} / {pages}" : "";
            _showAll.gameObject.SetActive(_filtered);
            _confirm.gameObject.SetActive(set.Mode == ChoiceMode.Multi);
            _confirm.interactable = set.IsValidCount(_toggled.Count);
            _finish.gameObject.SetActive(set.CanFinish);
            _cancel.gameObject.SetActive(set.CanCancel);
            // The same word as the dialog on the board for the same action: "Don't chain" on a chain, "Select" in the card
            // picker ("Confirm" when the picks are made on the field, as its button says).
            var cancelLabel = _cancel.GetComponentInChildren<Text>();
            cancelLabel.text = set.IsChain ? "Don't chain" : "Cancel";
            cancelLabel.resizeTextForBestFit = true; // the longer word fits the narrow column
            cancelLabel.resizeTextMinSize = 10;
            cancelLabel.resizeTextMaxSize = 14;
            _confirm.GetComponentInChildren<Text>().text = PromptDialogs.KindOf(set) == "field" ? "Confirm" : "Select";
            for (int i = 0; i < _pinned.Count; i++)
            {
                var (b, label) = _pinned[i];
                b.gameObject.SetActive(i < _pinnedChoices.Count);
                if (i < _pinnedChoices.Count) label.text = set.Choices[_pinnedChoices[i]].Label;
            }
            Refreshed?.Invoke();
        }

        /// <summary>A full-width (or partial) row positioned from the top of the panel.</summary>
        private RectTransform Row(string name, float top, float height, float xMin = 0.02f, float xMax = 0.98f)
        {
            var rt = Ui.Rect(name, _root);
            rt.anchorMin = new Vector2(xMin, 1);
            rt.anchorMax = new Vector2(xMax, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -top - height);
            rt.offsetMax = new Vector2(0, -top);
            return rt;
        }

        private Button SmallButton(string label, Vector2 xMin, Vector2 xMax, float top, Action onClick)
        {
            var rt = Row(label, top, 28, xMin.x, xMax.x);
            var image = Ui.Image(rt, Ui.Hex("#22324A"));
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { Sounds.Play("click"); onClick(); });
            Ui.Text(rt, label, 14, Color.white);
            UiHover.Attach(button);
            return button;
        }
    }
}
