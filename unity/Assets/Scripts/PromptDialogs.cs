using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Interactive;
using Ygo.Engine.Protocol;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Decisions shown in the middle of the board, Master Duel style, instead of only in the side list:
    /// <list type="bullet">
    /// <item>a card picker for choosing cards (Deck searches, sending to the GY, Tributes, materials): big cards grouped by
    /// where they are, checkmarks, a count (or Level stars), Select / Cancel;</item>
    /// <item>a chain dialog ("X was activated. Chain another card or effect?") with the cards that can respond;</item>
    /// <item>yes/no and "Use which effect?" dialogs.</item>
    /// </list>
    /// Every dialog can be hidden to look at the board. The side list (<see cref="PromptPanel"/>) stays the source of
    /// truth: these only call its ClickChoice / Confirm / Finish / Cancel, and redraw when it changes.
    /// </summary>
    public sealed class PromptDialogs
    {
        private static readonly Color Panel = new Color(0.05f, 0.07f, 0.12f, 0.96f);
        private static readonly Color Header = Ui.Hex("#13203A");
        private static readonly Color ChainHeader = Ui.Hex("#1B4A8C");
        private static readonly Color Gold = Ui.Hex("#FFC940");
        private static readonly Vector2 CardSize = new Vector2(118, 172);

        private readonly RectTransform _field;
        private readonly PromptPanel _prompt;
        private readonly ICardDataProvider _cards;
        private readonly Func<CardRef, FieldCard?> _live;
        private readonly Action<CardInfo> _hover;
        private RectTransform? _root;
        private ChoiceSet? _set;
        private bool _hidden;
        private float _scroll;

        public PromptDialogs(RectTransform field, PromptPanel prompt, ICardDataProvider cards, Func<CardRef, FieldCard?> live, Action<CardInfo> hover)
        {
            _field = field;
            _prompt = prompt;
            _cards = cards;
            _live = live;
            _hover = hover;
            // Toggling a card in a multi-pick (here or in the side list) redraws the checkmarks and count.
            _prompt.Refreshed += () => { if (_set != null && _prompt.Current == _set) Draw(); };
        }

        /// <summary>The dialog on screen right now ("picker", "chain", "yesno", "options"), or "" for none.</summary>
        public string Kind { get; private set; } = "";
        public bool Hidden => _hidden;

        /// <summary>The dialog on screen (Check Field hides it), or null.</summary>
        public RectTransform? Root => _root;

        private RectTransform? _banner;
        private readonly List<RectTransform> _discs = new List<RectTransform>();

        /// <summary>
        /// A field pick's Confirm / Cancel discs and their labels while they show (none otherwise): the board keeps its
        /// effect callouts clear of them.
        /// </summary>
        public IEnumerable<RectTransform> Discs
        {
            get
            {
                if (_root == null) yield break;
                var group = _root.GetComponent<CanvasGroup>();
                if (group != null && group.alpha <= 0) yield break;
                foreach (var disc in _discs)
                {
                    if (disc == null) continue;
                    yield return disc;
                    if (disc.Find("Label") is RectTransform label) yield return label;
                }
            }
        }

        /// <summary>
        /// The banner at the top of the board (a field pick's or a position's, or the hidden dialog's pill) while it shows,
        /// else null: the board lays its messages and effect callouts out below it.
        /// </summary>
        public RectTransform? Banner
        {
            get
            {
                if (_banner == null || _root == null) return null;
                var group = _root.GetComponent<CanvasGroup>();
                return group != null && group.alpha <= 0 ? null : _banner;
            }
        }

        /// <summary>
        /// What a chain prompt answers, when the board knows it ("Responding to: Pot of Greed", "Celtic Guardian attacks
        /// directly"), or "": shown under the chain dialog's title.
        /// </summary>
        public Func<string>? Responding;

        /// <summary>The dialogs' "check the field" button: the board's Check Field (when set) instead of hiding the dialog alone.</summary>
        public Action? CheckField;

        public void Show(ChoiceSet? set)
        {
            if (set != _set) { _hidden = false; _scroll = 0; _optionTab = 0; }
            _set = set;
            Draw();
        }

        /// <summary>Hides the dialog to look at the board (a pill at the top brings it back).</summary>
        public void SetHidden(bool hidden)
        {
            _hidden = hidden;
            Draw();
        }

        /// <summary>Which dialog suits a decision; "" leaves it to the board and the side list.</summary>
        public static string KindOf(ChoiceSet? set)
        {
            if (set == null) return "";
            if (set.IsChain) return "chain";
            switch (set.Prompt)
            {
                case SelectCardPrompt _:
                case TributePrompt _:
                case SelectUnselectPrompt _:
                case SumPrompt _:
                    if (set.Choices.Count == 0 || set.Choices.Any(c => c.Card == null)) return "";
                    // Cards that are all on the field are picked right there (they glow); anything else in the picker.
                    // Xyz materials count as elsewhere: they sit under their monster, so a click on it can't say which.
                    return set.Choices.All(c => (c.Card!.Value.Location & Location.Overlay) == 0
                        && (c.Card!.Value.Location & 0x7f) is Location.MZone or Location.SZone) ? "field" : "picker";
                case PositionPrompt _:
                    return "position";
                case EffectYesNoPrompt _:
                case YesNoPrompt _:
                    return "yesno";
                case OptionPrompt _:
                    return "options";
                case SortPrompt _:
                    return "order";
                default:
                    return "";
            }
        }

        private void Draw()
        {
            if (_root != null)
            {
                var old = _root.GetComponentInChildren<ScrollRect>();
                if (old != null) _scroll = old.horizontalNormalizedPosition;
                UnityEngine.Object.Destroy(_root.gameObject);
            }
            _root = null;
            _banner = null;
            _discs.Clear();
            Kind = KindOf(_set);
            if (_set == null || Kind == "") return;
            _root = Ui.Stretched("PromptDialog", _field, Vector2.zero, Vector2.one);
            if (_hidden)
            {
                // Peeking at the board: a pill at the top brings the dialog back.
                var pill = Ui.Placed("ShowChoices", _root, new Vector2(0, 470), new Vector2(420, 50));
                _banner = pill;
                Ui.Image(pill, ChainHeader);
                Ui.Text(pill, $"{Highlight(Short(_set.Title))}   ▲ Show", 17, Color.white).supportRichText = true;
                Ui.Clickable(pill, () => SetHidden(false), style: HoverStyle.Button);
                return;
            }
            // Dialogs dim the board a little; field picks and positions leave it clear (and clickable).
            if (Kind != "field" && Kind != "position") Ui.Image(_root, new Color(0, 0, 0, 0.35f));
            switch (Kind)
            {
                case "field": DrawFieldPick(_set); break;
                case "position": DrawPosition(_set); break;
                case "picker": DrawPicker(_set); break;
                case "chain": DrawChain(_set); break;
                case "yesno": DrawYesNo(_set); break;
                case "options": DrawOptions(_set); break;
                case "order": DrawOrder(_set); break;
            }
            _root.SetAsLastSibling();
        }

        // ------------------------------------------------------------------ Dialogs

        private void DrawPicker(ChoiceSet set)
        {
            var box = Frame(1180, 470, set.Title, Header);
            int me = set.Prompt.Player;
            // Groups in a fixed order: hand, your field, their field, Graveyards, banished, Deck, Extra Deck, materials.
            var groups = Enumerable.Range(0, set.Choices.Count)
                .GroupBy(i => GroupKey(set.Choices[i].Card!.Value, me))
                .OrderBy(g => g.Key.order)
                .ToList();
            float width = groups.Sum(g => g.Count() * (CardSize.x + 12) + 24);
            var view = Ui.Placed("Cards", box, new Vector2(0, 10), new Vector2(1120, 250));
            view.gameObject.AddComponent<RectMask2D>();
            Ui.Image(view, new Color(0, 0, 0, 0.001f));
            var content = Ui.Rect("Content", view);
            content.anchorMin = content.anchorMax = new Vector2(0, 0.5f);
            content.pivot = new Vector2(0, 0.5f);
            float contentWidth = Mathf.Max(width, 1120);
            content.sizeDelta = new Vector2(contentWidth, 250);
            // Children are placed from the content's center; x runs from its left edge.
            float x = Mathf.Max(0, (1120 - width) / 2) + 12 - contentWidth / 2;
            foreach (var g in groups)
            {
                var tab = Ui.Placed("Tab", content, new Vector2(x + 70, 106), new Vector2(160, 26));
                Ui.Image(tab, ChainHeader);
                Ui.Text(tab, g.Key.name, 14, Color.white).fontStyle = FontStyle.Bold;
                foreach (var i in g)
                {
                    var center = new Vector2(x + CardSize.x / 2, -12);
                    CardTile(content, center, set, i, me);
                    x += CardSize.x + 12;
                }
                x += 24;
            }
            if (width > 1120)
            {
                var scroll = view.gameObject.AddComponent<ScrollRect>();
                scroll.viewport = view;
                scroll.content = content;
                scroll.vertical = false;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                scroll.scrollSensitivity = 40;
                scroll.horizontalNormalizedPosition = _scroll;
                Ui.Button(box, "<", new Vector2(-575, 10), new Vector2(36, 80), () => scroll.horizontalNormalizedPosition = Mathf.Clamp01(scroll.horizontalNormalizedPosition - 0.3f));
                Ui.Button(box, ">", new Vector2(575, 10), new Vector2(36, 80), () => scroll.horizontalNormalizedPosition = Mathf.Clamp01(scroll.horizontalNormalizedPosition + 0.3f));
            }

            var status = Ui.Text(Ui.Placed("Status", box, new Vector2(0, -150), new Vector2(1000, 34)), Status(set), 18, Color.white);
            status.supportRichText = true;
            Buttons(box, Actions(set), -200);
        }

        /// <summary>What's picked so far: Level stars for materials, else a count.</summary>
        private string Status(ChoiceSet set)
        {
            if (set.Prompt is SumPrompt sum)
            {
                int total = Enumerable.Range(0, set.Choices.Count).Where(_prompt.IsSelected).Sum(i => (int)(sum.Values[i] & 0xffff));
                int target = sum.Target;
                var stars = string.Concat(Enumerable.Range(0, Mathf.Clamp(Mathf.Max(target, total), 1, 14))
                    .Select(k => k < total ? (k < target ? "<color=#FFC940>★</color>" : "<color=#FF7070>★</color>") : "<color=#5A6070>★</color>"));
                return $"{stars}   Levels {total} / {target}{(sum.Exact ? "" : "+")}";
            }
            else if (set.Mode == ChoiceMode.Multi)
            {
                int picked = Enumerable.Range(0, set.Choices.Count).Count(_prompt.IsSelected);
                return set.Min == set.Max ? $"Selected <color=#FFC940>{picked}</color> of {set.Max}" : $"Selected <color=#FFC940>{picked}</color> (choose {set.Min} to {set.Max})";
            }
            return set.Detail;
        }

        /// <summary>Select (multi-picks), Done (when allowed) and Cancel (when allowed).</summary>
        private List<(string label, Action act, bool primary, bool enabled)> Actions(ChoiceSet set)
        {
            var buttons = new List<(string label, Action act, bool primary, bool enabled)>();
            if (set.Mode == ChoiceMode.Multi) buttons.Add(("Select", _prompt.Confirm, true, set.IsValidCount(Enumerable.Range(0, set.Choices.Count).Count(_prompt.IsSelected))));
            if (set.CanFinish) buttons.Add(("Done", _prompt.FinishSelection, true, true));
            if (set.CanCancel) buttons.Add(("Cancel", _prompt.CancelSelection, false, true));
            return buttons;
        }

        /// <summary>
        /// Picking cards on the field: they glow on the board (click them there); a banner at the top says what to pick,
        /// with the count and Select / Cancel. Picked cards get a checkmark on the board.
        /// </summary>
        private void DrawFieldPick(ChoiceSet set)
        {
            // Left of center, so the effect text at the top right (what is resolving) stays readable.
            var banner = Ui.Placed("Banner", _root!, new Vector2(-150, 440), new Vector2(800, 120));
            _banner = banner;
            Ui.Image(banner, Panel);
            var edge = Ui.Placed("Edge", banner, new Vector2(0, -58), new Vector2(800, 4));
            Ui.Image(edge, Gold);
            var title = Ui.Text(Ui.Placed("Title", banner, new Vector2(-24, 26), new Vector2(700, 44)), Highlight(set.Title), 24, Color.white);
            title.supportRichText = true;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 14;
            title.resizeTextMaxSize = 24;
            var status = Ui.Text(Ui.Placed("Status", banner, new Vector2(0, -24), new Vector2(760, 36)),
                $"{Status(set)}   <color=#9AA4B8>Click a glowing card.</color>", 18, Color.white);
            status.supportRichText = true;
            if (CheckField != null)
            {
                // The banner covers the opponent's back row: a button to see under it (or hold Space).
                var peek = Ui.Button(banner, "▼", new Vector2(372, 26), new Vector2(44, 40), CheckField);
                var tip = peek.gameObject.AddComponent<TipOnHover>();
                tip.Title = "Check field";
                tip.Body = "Hides this to look at the board. Hold Space to peek instead.";
            }
            var actions = Actions(set);
            for (int i = 0; i < actions.Count; i++)
            {
                var (label, act, primary, enabled) = actions[i];
                if (!enabled) continue;
                _discs.Add(Icons.Disc(_root!, new Vector2(470 - i * 120, 300), label == "Select" ? "Confirm" : label, act, 84, primary));
            }
        }

        /// <summary>Choosing a position: the card with a round button for each position above it.</summary>
        private void DrawPosition(ChoiceSet set)
        {
            var banner = Ui.Placed("Banner", _root!, new Vector2(-150, 440), new Vector2(800, 70));
            _banner = banner;
            Ui.Image(banner, Panel);
            Ui.Text(banner, Highlight(set.Title), 22, Color.white).supportRichText = true;
            if (set.Prompt is PositionPrompt pp && _cards.Get(pp.Code) is { } info)
            {
                var card = new FieldCard { Code = info.Code, Position = 0x1, Type = info.Type, Level = info.Level & 0xff, Attack = info.Attack, Defense = info.Defense };
                CardView.Create(_root!, new Vector2(0, -230), new Vector2(140, 204), card, info, 0, true, _hover, offField: true);
            }
            int n = set.Choices.Count;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                var label = set.Choices[i].Label.Replace(" Position", "");
                Icons.Disc(_root!, new Vector2((i - (n - 1) / 2f) * 200, -20), label, () => _prompt.ClickChoice(index), 100, i == 0);
            }
        }

        private void DrawChain(ChoiceSet set)
        {
            // "Your opponent activated X. Respond?" reads as "... activated X. Chain another card or effect?"
            var title = set.Title.EndsWith(" Respond?") ? set.Title.Replace(" Respond?", " Chain another card or effect?") : set.Title;
            int count = set.Choices.Count;
            float boxWidth = Mathf.Clamp(count * 150 + 120, 760, 1180);
            // What you'd be answering, when the title doesn't already say ("... activated X"): a line under the header.
            var about = title.Contains("activated") ? "" : Responding?.Invoke() ?? "";
            float boxHeight = about.Length > 0 ? 468 : 440;
            var box = Frame(boxWidth, boxHeight, title, ChainHeader);
            if (about.Length > 0)
            {
                var line = Ui.Text(Ui.Placed("Responding", box, new Vector2(0, boxHeight / 2 - 94), new Vector2(boxWidth - 80, 24)), about, 16,
                    new Color(0.75f, 0.85f, 1f));
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 12;
                line.resizeTextMaxSize = 16;
            }
            int me = set.Prompt.Player;
            float x0 = -(count - 1) * 150 / 2f;
            // What a pick becomes: the next Chain Link, which resolves before every link already on the chain.
            int next = set.ChainLength + 1;
            for (int i = 0; i < count; i++)
            {
                // The cards sit a little high so the hint under them keeps clear of the button.
                var center = new Vector2(x0 + i * 150, 30);
                CardTile(box, center, set, i, me);
                var verb = set.Choices[i].Verb.Length > 0 ? set.Choices[i].Verb : "Activate";
                Ui.Text(Ui.Placed("Verb", box, center + new Vector2(0, -100), new Vector2(146, 24)), verb, 14, Gold);
                Ui.Text(Ui.Placed("Link", box, center + new Vector2(0, -122), new Vector2(146, 22)), $"→ Chain Link {next}", 14, new Color(0.7f, 0.82f, 1f));
            }
            var buttons = new List<(string, Action, bool, bool)>();
            if (set.CanCancel) buttons.Add(("Don't chain", _prompt.CancelSelection, false, true));
            Buttons(box, buttons, -180);
            string how = count == 1
                ? next == 1 ? "Click the card to activate it." : $"Click the card to activate it: as Chain Link {next} it resolves first."
                : next == 1 ? "Several can activate: the one you pick first becomes Chain Link 1 and resolves last; the next one chained resolves before it."
                : $"Click a card to activate it: as Chain Link {next} it resolves first, before Chain Link {next - 1}.";
            // Between the cards' captions (bottom -103) and the button (top -153): two lines at most, 12 px above the button.
            var howText = Ui.Text(Ui.Placed("How", box, new Vector2(0, -123), new Vector2(boxWidth - 60, 36)), how, 16, new Color(1, 1, 1, 0.65f));
            howText.horizontalOverflow = HorizontalWrapMode.Wrap;
            howText.resizeTextForBestFit = true;
            howText.resizeTextMinSize = 12;
            howText.resizeTextMaxSize = 16;
        }

        /// <summary>
        /// Putting cards in order (two mandatory effects at once, or cards going on top of the Deck): click them first to
        /// last. Each shows its place: for a chain, its Chain Link and when it resolves (the last link resolves first).
        /// </summary>
        private void DrawOrder(ChoiceSet set)
        {
            bool chain = set.Prompt is SortPrompt sp && sp.IsChain;
            int count = set.Choices.Count;
            var box = Frame(Mathf.Clamp(count * 160 + 160, 940, 1180), 460, set.Title, chain ? ChainHeader : Header);
            int me = set.Prompt.Player;
            float x0 = -(count - 1) * 160 / 2f;
            int total = set.ChainLength + count;
            for (int i = 0; i < count; i++)
            {
                var center = new Vector2(x0 + i * 160, 40);
                CardTile(box, center, set, i, me);
                int place = _prompt.OrderOf(i);
                if (place < 0) continue;
                var badge = Ui.Placed("Place", box, center + new Vector2(-CardSize.x / 2 + 16, CardSize.y / 2 - 16), new Vector2(34, 34));
                var disc = badge.gameObject.AddComponent<RawImage>();
                disc.texture = Icons.Round;
                disc.color = Gold;
                disc.raycastTarget = false;
                var n = Ui.Text(badge, (place + 1).ToString(), 18, Ui.Hex("#1A1408"));
                n.fontStyle = FontStyle.Bold;
                n.raycastTarget = false;
                int link = set.ChainLength + place + 1, resolves = total - link + 1;
                string caption = chain ? $"Chain Link {link} · resolves {Ordinal(resolves)}" : place == 0 ? "on top" : $"{Ordinal(place + 1)} from the top";
                Ui.Text(Ui.Placed("Caption", box, center + new Vector2(0, -CardSize.y / 2 - 16), new Vector2(156, 22)), caption, 13, Gold);
            }
            int placed = Enumerable.Range(0, count).Count(i => _prompt.OrderOf(i) >= 0);
            var status = Ui.Text(Ui.Placed("Status", box, new Vector2(0, -120), new Vector2(1060, 46)),
                placed < count ? $"<color=#FFC940>{placed}</color> of {count} placed.  {set.Detail}" : "All placed. Confirm, or click one to take it back out.", 15, Color.white);
            status.supportRichText = true;
            var buttons = new List<(string, Action, bool, bool)>
            {
                ("Confirm", _prompt.Confirm, true, placed == count),
                ("Start over", _prompt.ResetOrder, false, placed > 0),
                ("Default order", _prompt.FinishSelection, false, set.CanFinish),
            };
            Buttons(box, buttons, -190);
        }

        private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        private void DrawYesNo(ChoiceSet set)
        {
            bool withCard = set.Prompt is EffectYesNoPrompt;
            var box = Frame(820, withCard ? 440 : 280, set.Title, Header);
            if (set.Prompt is EffectYesNoPrompt ep)
            {
                var info = _cards.Get(ep.Card.Code);
                if (info != null)
                {
                    var card = new FieldCard { Code = info.Code, Position = 0x1, Type = info.Type, Level = info.Level & 0xff, Attack = info.Attack, Defense = info.Defense };
                    CardView.Create(box, new Vector2(-250, 10), CardSize, card, info, ep.Card.Controller & 1, true, _hover, offField: true);
                }
            }
            // The effect's own description when the game gives one, else the card's text.
            var words = set.Detail;
            if (words.Length == 0 && set.Prompt is EffectYesNoPrompt yp) words = _cards.Get(yp.Card.Code)?.Text ?? "";
            var detail = Ui.Text(Ui.Placed("Detail", box, withCard ? new Vector2(80, 10) : new Vector2(0, 10), withCard ? new Vector2(520, 230) : new Vector2(740, 120)),
                Highlight(words), 18, new Color(0.85f, 0.88f, 0.94f), withCard ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            detail.supportRichText = true;
            detail.resizeTextForBestFit = true;
            detail.resizeTextMinSize = 11;
            detail.resizeTextMaxSize = FxSettings.Scaled(18);
            Buttons(box, new List<(string, Action, bool, bool)>
            {
                ("Yes", () => _prompt.ClickChoice(0), true, true),
                ("No", () => _prompt.ClickChoice(1), false, true),
            }, withCard ? -180 : -100);
        }

        /// <summary>"Use which effect?": a numbered tab per effect, the chosen one's text, and OK.</summary>
        private void DrawOptions(ChoiceSet set)
        {
            int n = set.Choices.Count;
            _optionTab = Mathf.Clamp(_optionTab, 0, n - 1);
            var box = Frame(900, 430, set.Title == "Choose an effect" ? "Use which effect?" : set.Title, Header);
            float tabW = Mathf.Min(300, 840f / n);
            float x0 = -(n - 1) * tabW / 2;
            for (int i = 0; i < n; i++)
            {
                int index = i;
                bool on = i == _optionTab;
                var tab = Ui.Placed($"Tab{i}", box, new Vector2(x0 + i * tabW, 110), new Vector2(tabW - 10, 50));
                Ui.Image(tab, on ? Ui.Hex("#9BD12E") : Ui.Hex("#13304F"));
                var underline = Ui.Placed("Line", tab, new Vector2(0, -24), new Vector2(tabW - 10, 3));
                Ui.Image(underline, Ui.Hex("#9BD12E")).raycastTarget = false;
                var number = Ui.Text(tab, (i + 1).ToString(), 24, on ? Ui.Hex("#0B1A10") : Color.white);
                number.fontStyle = FontStyle.Bold;
                Ui.Clickable(tab, () => SelectTab(index), style: on ? HoverStyle.None : HoverStyle.Button); // the open tab doesn't light up
            }
            var body = Ui.Placed("Body", box, new Vector2(0, -25), new Vector2(840, 190));
            Ui.Image(body, Ui.Hex("#0F2236"));
            var text = Ui.Text(body, Highlight(set.Choices[_optionTab].Label), 24, Color.white, TextAnchor.UpperLeft);
            text.supportRichText = true;
            text.rectTransform.offsetMin = new Vector2(24, 16);
            text.rectTransform.offsetMax = new Vector2(-24, -20);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = FxSettings.Scaled(24);
            int chosen = _optionTab;
            Buttons(box, new List<(string, Action, bool, bool)> { ("OK", () => _prompt.ClickChoice(chosen), true, true) }, -170);
        }

        private int _optionTab;

        /// <summary>Shows an effect's tab in the "Use which effect?" dialog (clicking it, or a check).</summary>
        public void SelectTab(int index)
        {
            _optionTab = index;
            Draw();
        }

        // ------------------------------------------------------------------ Pieces

        /// <summary>A dialog box centered on the board with a header line and a Hide button.</summary>
        private RectTransform Frame(float width, float height, string title, Color header)
        {
            var box = Ui.Placed("Box", _root!, new Vector2(0, 20), new Vector2(width, height));
            Ui.Image(box, Panel);
            var outline = box.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.35f, 0.6f, 1f, 0.7f);
            outline.effectDistance = new Vector2(2, -2);
            var head = Ui.Placed("Header", box, new Vector2(0, height / 2 - 40), new Vector2(width, 80));
            Ui.Image(head, header);
            var text = Ui.Text(head, Highlight(title), 22, Color.white);
            text.supportRichText = true;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 14;
            text.resizeTextMaxSize = 22;
            text.rectTransform.offsetMin = new Vector2(30, 0);
            text.rectTransform.offsetMax = new Vector2(-90, 0);
            var hide = Ui.Button(head, "▼", new Vector2(width / 2 - 40, 0), new Vector2(56, 50), () => { if (CheckField != null) CheckField(); else SetHidden(true); });
            hide.GetComponentInChildren<Text>().fontSize = 22;
            var tip = hide.gameObject.AddComponent<TipOnHover>();
            tip.Title = "Check field";
            tip.Body = "Hides this to look at the board. Hold Space to peek instead.";
            return box;
        }

        /// <summary>A big clickable card for a choice, gold-framed with a checkmark when it is picked.</summary>
        private void CardTile(RectTransform parent, Vector2 center, ChoiceSet set, int index, int me)
        {
            var c = set.Choices[index].Card!.Value;
            var info = _cards.Get(c.Code);
            var loc = c.Location & 0x7f;
            var live = (loc == Location.MZone || loc == Location.SZone) ? _live(c) : null;
            var card = live ?? new FieldCard
            {
                Code = c.Code, Position = 0x1, Type = info?.Type ?? 0, Level = (info?.Level ?? 0) & 0xff,
                Attack = info?.Attack ?? 0, Defense = info?.Defense ?? 0,
            };
            // Face-down cards on the opponent's field stay hidden; anything else you are choosing from, you can see.
            bool hidden = live != null && !live.IsFaceUp && c.Controller != me;
            bool picked = _prompt.IsSelected(index) || set.Choices[index].AlreadySelected;
            CardView.Create(parent, center, CardSize, card, hidden ? null : info, c.Controller & 1, !hidden, _hover, offField: true,
                onClick: () => { if (set.Mode != ChoiceMode.Single && ClickGuard.Repeat(set, index)) return; Sounds.Play("click"); _prompt.ClickChoice(index); }, highlight: picked);
            if (picked)
            {
                var check = Ui.Placed("Check", parent, center + new Vector2(CardSize.x / 2 - 14, CardSize.y / 2 - 14), new Vector2(36, 36));
                check.localRotation = Quaternion.Euler(0, 0, 45);
                Ui.Image(check, Ui.Hex("#13203A")).raycastTarget = false;
                var mark = Ui.Text(Ui.Placed("Mark", parent, check.anchoredPosition, new Vector2(36, 36)), "✓", 24, Gold);
                mark.raycastTarget = false;
            }
        }

        private void Buttons(RectTransform box, List<(string label, Action act, bool primary, bool enabled)> buttons, float y)
        {
            float w = 260, gap = 30;
            float x0 = -(buttons.Count - 1) * (w + gap) / 2;
            for (int i = 0; i < buttons.Count; i++)
            {
                var (label, act, primary, enabled) = buttons[i];
                var b = Ui.Button(box, label, new Vector2(x0 + i * (w + gap), y), new Vector2(w, 54), () => { Sounds.Play("click"); act(); });
                b.targetGraphic.color = primary ? Ui.Hex("#2F6B2A") : Ui.Hex("#2A3140");
                var text = b.GetComponentInChildren<Text>();
                text.fontSize = 20;
                text.color = primary ? Ui.Hex("#E8FF8A") : Color.white;
                b.interactable = enabled;
            }
        }

        private static (int order, string name) GroupKey(CardRef c, int me)
        {
            bool mine = c.Controller == me;
            switch (c.Location & 0x7f)
            {
                case Location.Hand: return (mine ? 0 : 1, mine ? "Your hand" : "Their hand");
                case Location.MZone:
                case Location.SZone: return (mine ? 2 : 3, mine ? "Your field" : "Their field");
                case Location.Grave: return (mine ? 4 : 5, mine ? "Your GY" : "Their GY");
                case Location.Removed: return (mine ? 6 : 7, mine ? "Your banished" : "Their banished");
                case Location.Deck: return (mine ? 8 : 9, mine ? "Your Deck" : "Their Deck");
                case Location.Extra: return (mine ? 10 : 11, mine ? "Your Extra Deck" : "Their Extra Deck");
                default: return (12, "Materials");
            }
        }

        private static string Short(string title) => title.Length > 38 ? title.Substring(0, 36) + "…" : title;

        // ------------------------------------------------------------------ Gold key words

        private static readonly Regex Quoted = new Regex("\"([^\"]+)\"");
        // "activated Mystical Space Typhoon." / "the effect of Diabellstar the Black Witch?"
        private static readonly Regex Named = new Regex(@"(?<=\bactivated |\beffect of )([^.?""]+)(?=[.?])");
        private static readonly Regex Keywords = new Regex(
            @"\b(card\(s\)|cards?|monster\(s\)|monsters?|Graveyard|GY|Deck|Extra Deck|hand|field|banish(?:ed)?|activated?|Chain|chain|Tribute|Special Summon(?:ed)?|Normal Summon(?:ed)?|Summon|Set|destroy(?:ed)?|target|materials?|Levels?|Spell|Trap|effect)\b");

        /// <summary>Key words in gold, as in Master Duel: "Select the <b>card</b> to send to the <b>Graveyard</b>."</summary>
        public static string Highlight(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Contains("<color")) return text;
            // Card names you're asked about ("activated X.", "the effect of X?") in bold gold; key words around them in gold.
            var result = new System.Text.StringBuilder();
            int at = 0;
            foreach (Match name in Named.Matches(text))
            {
                result.Append(Words(text.Substring(at, name.Index - at)));
                result.Append($"<b><color=#FFC940>{name.Value}</color></b>");
                at = name.Index + name.Length;
            }
            result.Append(Words(text.Substring(at)));
            return result.ToString();

            static string Words(string part)
            {
                part = Quoted.Replace(part, m => $"\"<color=#FFC940>{m.Groups[1].Value}</color>\"");
                return Keywords.Replace(part, m => $"<color=#FFC940>{m.Value}</color>");
            }
        }
    }
}
