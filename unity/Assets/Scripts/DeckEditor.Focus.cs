using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Click to focus (docs/dev/EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md): pointing at a card only lifts it; a click focuses it
    /// (a white ring, and it shows on the left); the focused card clicked again adds it (results) or takes that copy out
    /// (deck); Shift+click does it at once; right-click is the Side Deck action. Keys act on the focus, the wheel scrolls the
    /// focused card's text from anywhere but what scrolls on its own. Focus is remembered by data, never by view: the grids
    /// are rebuilt after every change, and focusing alone only moves the ring.
    /// </summary>
    public sealed partial class DeckEditor
    {
        private enum FocusZone { None, Results, Deck, Hand }

        /// <summary>Where the focus is: a result (by base card), a deck copy (section, base card, which copy of it there), a test hand card.</summary>
        private FocusZone _focusZone;
        private uint _focusBase;
        private DeckSection _focusSection;
        private int _focusCopy, _focusHand;

        /// <summary>The views drawn now (rebuilt with their grids), each with where it sits at rest.</summary>
        private readonly List<(DeckSection section, int index, CardView view, Vector2 home)> _deckViews = new List<(DeckSection, int, CardView, Vector2)>();
        private readonly List<(uint key, CardView view, int plates, Vector2 home)> _resultViews = new List<(uint, CardView, int, Vector2)>();
        private readonly List<CardView> _handViews = new List<CardView>();
        private GameObject? _ring;

        /// <summary>A tile to shake or flash: a deck copy (section, base card, which copy) or a result (base card).</summary>
        private readonly struct TileRef
        {
            public readonly bool Deck;
            public readonly DeckSection Section;
            public readonly uint Key;
            public readonly int Copy;

            public TileRef(bool deck, DeckSection section, uint key, int copy)
            {
                Deck = deck;
                Section = section;
                Key = key;
                Copy = copy;
            }

            public static TileRef Result(uint key) => new TileRef(false, DeckSection.Main, key, 0);
        }

        private uint Key(uint code) => Banlist.BaseCode(code, _cards);

        /// <summary>Which copy of its card (by base card) the slot at <paramref name="index"/> is in its section: 0 for the first.</summary>
        private int CopyOrdinal(List<uint> list, int index)
        {
            var key = Key(list[index]);
            int n = 0;
            for (int i = 0; i < index; i++) if (Key(list[i]) == key) n++;
            return n;
        }

        /// <summary>The slot holding copy <paramref name="copy"/> of <paramref name="key"/> in a section, or -1.</summary>
        private int SlotOf(DeckSection section, uint key, int copy)
        {
            var list = Section(_deck, section);
            for (int i = 0, seen = 0; i < list.Count; i++)
            {
                if (Key(list[i]) != key) continue;
                if (seen == copy) return i;
                seen++;
            }
            return -1;
        }

        private TileRef DeckTile(DeckSection section, int index) => new TileRef(true, section, Key(Section(_deck, section)[index]), CopyOrdinal(Section(_deck, section), index));

        /// <summary>The focused deck copy's slot, or -1 (the focus isn't in the deck, or that copy is gone).</summary>
        private int FocusedSlot() => _focusZone == FocusZone.Deck ? SlotOf(_focusSection, _focusBase, _focusCopy) : -1;

        // ---- Focusing (moves the ring and shows the card on the left; never rebuilds a grid) -------------------------------

        /// <summary>
        /// Where the focus was armed: the tile ("R" and the card's place in the results, or "D", the section and slot) the
        /// player clicked to focus. Clicking the focused card again acts only while it's armed and the card is still on that
        /// tile; a focus the editor set (on open, after a removal, after the test hand) or the keys set isn't armed, and a
        /// card that moved (a card before it went, a new search put it elsewhere) isn't on its tile any more. The keys act on
        /// any focus. (Owner's rule, 2026-11-19: no accidental moving of cards.)
        /// </summary>
        private string? _armedAt;

        private static string ResultTile(int index) => $"R{index}";
        private static string DeckTileKey(DeckSection section, int index) => $"D{(int)section}:{index}";

        private void FocusResult(CardInfo info, bool viewed)
        {
            _armedAt = null;
            _focusZone = FocusZone.Results;
            _focusBase = Key(info.Code);
            if (viewed) Viewed(info);
            ShowDetail(info);
            ShowRing();
        }

        private void FocusDeck(DeckSection section, int index, bool viewed)
        {
            var list = Section(_deck, section);
            if (index < 0 || index >= list.Count) return;
            _armedAt = null;
            _focusZone = FocusZone.Deck;
            _focusSection = section;
            _focusBase = Key(list[index]);
            _focusCopy = CopyOrdinal(list, index);
            if (_cards.Get(list[index]) is CardInfo info)
            {
                if (viewed) Viewed(info);
                ShowDetail(info);
            }
            ShowRing();
        }

        private void FocusHand(int index, bool viewed)
        {
            if (_testHand == null || index < 0 || index >= _testHand.Count) return;
            _armedAt = null;
            _focusZone = FocusZone.Hand;
            _focusHand = index;
            if (_cards.Get(_testHand[index]) is CardInfo info)
            {
                if (viewed) Viewed(info);
                ShowDetail(info);
            }
            ShowRing();
        }

        /// <summary>The test hand closed: a focus on one of its cards goes to that card's result (not armed; ringed when its page shows).</summary>
        private void CloseTestHand()
        {
            _handOverlay.gameObject.SetActive(false);
            if (_focusZone != FocusZone.Hand) return;
            _focusZone = _testHand != null && _focusHand < _testHand.Count ? FocusZone.Results : FocusZone.None;
            if (_focusZone == FocusZone.Results) _focusBase = Key(_testHand![_focusHand]);
            _armedAt = null;
            ShowRing();
        }

        /// <summary>
        /// The ring round the focused card, if it's drawn: outside the card in <see cref="UiTheme.CardFocus"/>, with a gap of
        /// the background between (deck 1 + 3 px; results 2 + 3 px round the card and its plates; test hand 2 + 4 px).
        /// </summary>
        private void ShowRing()
        {
            var T = UiTheme.Current;
            if (_ring != null) Destroy(_ring);
            _ring = null;
            CardView? view = null;
            float gap = 0, width = 3, right = 0, down = 0;
            Color bg = T.Base;
            switch (_focusZone)
            {
                case FocusZone.Results:
                    foreach (var r in _resultViews)
                        if (r.key == _focusBase && r.view != null) { view = r.view; right = 4 * r.plates; down = 2 * r.plates; break; }
                    gap = 2;
                    bg = T.Panel;
                    break;
                case FocusZone.Deck:
                    int slot = FocusedSlot();
                    foreach (var d in _deckViews)
                        if (d.section == _focusSection && d.index == slot && d.view != null) { view = d.view; break; }
                    gap = 1;
                    break;
                case FocusZone.Hand:
                    if (_handOverlay.gameObject.activeSelf && _focusHand < _handViews.Count) view = _handViews[_focusHand];
                    gap = 2;
                    width = 4;
                    bg = T.GlassSoft;
                    break;
            }
            if (view == null) return;
            float o = gap + width;
            var holder = Ui.Stretched("FocusRing", view.transform, Vector2.zero, Vector2.one);
            holder.offsetMin = new Vector2(-o, -o - down);
            holder.offsetMax = new Vector2(o + right, o);
            var ring = holder.gameObject.AddComponent<FrameGraphic>();
            ring.raycastTarget = false;
            ring.Thickness = width;
            ring.color = T.CardFocus;
            FrameGraphic.Make("Gap", holder, -width, gap, bg);
            _ring = holder.gameObject;
            // Pointed at, the card is drawn lifted: the ring lifts with it at once.
            if (view.TryGetComponent<UiHover>(out var hover)) hover.Adopt(_ring);
        }

        // ---- Clicks -------------------------------------------------------------------------------------------------------

        private static bool ShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
                if (device is UnityEngine.InputSystem.Keyboard k && k.enabled && k.shiftKey.isPressed) return true;
            return false;
#else
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif
        }

        /// <summary>
        /// A search result (at <paramref name="index"/> in the results) clicked: focus it; the focused one clicked again on
        /// the tile it was armed on adds it (a focus that isn't armed is armed first); Shift+click adds at once; right-click:
        /// the Side Deck.
        /// </summary>
        private void ClickResult(CardInfo info, int index, PointerEventData.InputButton button)
        {
            if (button == PointerEventData.InputButton.Middle) return;
            if (button == PointerEventData.InputButton.Right) { Add(info, side: true); return; }
            if (ShiftHeld()) { Add(info, side: false); return; }
            bool focused = _focusZone == FocusZone.Results && _focusBase == Key(info.Code);
            if (focused && _armedAt == ResultTile(index)) { Add(info, side: false); return; }
            if (!focused) FocusResult(info, viewed: true);
            _armedAt = ResultTile(index);
        }

        /// <summary>
        /// A deck card clicked: focus it; the focused copy clicked again on the slot it was armed on takes it out (a focus
        /// that isn't armed, or a card that moved since, is armed first); Shift+click takes it out at once; right-click moves
        /// it between Main/Extra and Side.
        /// </summary>
        private void ClickDeck(DeckSection section, int index, PointerEventData.InputButton button)
        {
            if (button == PointerEventData.InputButton.Middle) return;
            if (button == PointerEventData.InputButton.Right) { MoveSection(section, index); return; }
            if (ShiftHeld()) { RemoveAt(section, index); return; }
            bool focused = _focusZone == FocusZone.Deck && _focusSection == section && FocusedSlot() == index;
            if (focused && _armedAt == DeckTileKey(section, index)) { RemoveAt(section, index); return; }
            if (!focused) FocusDeck(section, index, viewed: true);
            _armedAt = DeckTileKey(section, index);
        }

        /// <summary>A test hand card: a click focuses it (to read); nothing else does anything.</summary>
        private void ClickHand(int index, PointerEventData.InputButton button)
        {
            if (button == PointerEventData.InputButton.Left && !ShiftHeld() && !(_focusZone == FocusZone.Hand && _focusHand == index)) FocusHand(index, viewed: true);
        }

        /// <summary>
        /// Enter in the search box: the first result is focused (not armed: Enter again adds it, a click arms it), and the box
        /// is let go: left selected, the next Enter (Submit) would open it again as well as add the card.
        /// </summary>
        private void SearchEntered()
        {
            if (_results.Count > 0) FocusResult(_results[0], viewed: false);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _searchField.gameObject) EventSystem.current.SetSelectedGameObject(null!);
        }

        // ---- Focus bookkeeping as the deck changes --------------------------------------------------------------------------

        /// <summary>
        /// Before slot <paramref name="index"/> of <paramref name="section"/> goes: whether it's the focused copy (then
        /// <see cref="RefocusAfterRemoval"/> moves the ring once it's gone), else the focused copy's number shifts down if it
        /// came after a copy of the same card.
        /// </summary>
        private bool FocusLeaving(DeckSection section, int index)
        {
            var list = Section(_deck, section);
            if (_focusZone != FocusZone.Deck || _focusSection != section || index < 0 || index >= list.Count || Key(list[index]) != _focusBase) return false;
            int ordinal = CopyOrdinal(list, index);
            if (ordinal == _focusCopy) return true;
            if (ordinal < _focusCopy) _focusCopy--;
            return false;
        }

        /// <summary>
        /// The focused copy was taken out of slot <paramref name="index"/>: the ring goes to the next copy of that card in the
        /// section (the one before when it was the last), never the card that slid into the gap; else to its result tile;
        /// else nowhere. The left panel keeps the card (Enter adds it back).
        /// </summary>
        private void RefocusAfterRemoval(DeckSection section, int index, uint key)
        {
            var list = Section(_deck, section);
            int pick = -1;
            for (int i = index + 1; i < list.Count && pick < 0; i++) if (Key(list[i]) == key) pick = i;
            for (int i = index - 1; i >= 0 && pick < 0; i--) if (Key(list[i]) == key) pick = i;
            _armedAt = null; // the editor moved the focus: the next click on it only arms it
            if (pick >= 0)
            {
                _focusCopy = CopyOrdinal(list, pick);
            }
            else if (_results.Any(c => Key(c.Code) == key))
            {
                _focusZone = FocusZone.Results;
                _focusBase = key;
            }
            else _focusZone = FocusZone.None;
            ShowRing();
        }

        // ---- Keys, the wheel and the right stick (Update) -------------------------------------------------------------------

        private bool _typingLastFrame;

        /// <summary>A text field has the keyboard (the editor's: search box, deck name, label, Archetype, the save-as name): the UI's selection is a focused InputField.</summary>
        private static bool Typing()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent<InputField>(out var field) && field.isFocused;
        }

        /// <summary>The focus's keys, the wheel and the stick; and no editor button stays selected for Enter or Space to press again.</summary>
        private void FocusInput()
        {
            // A clicked button stays the UI's selection, and Enter (Submit) would press it again: the keys here are the focus's.
            var events = EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(_root) && selected.GetComponent<InputField>() == null) events!.SetSelectedGameObject(null!);

            bool typing = Typing();
            bool blocked = typing || _typingLastFrame || _reading != null || _browser != null;
            _typingLastFrame = typing;
            if (!blocked) Keys();
            if (_reading == null) Wheel();
        }

#if ENABLE_INPUT_SYSTEM
        private static bool Down(UnityEngine.InputSystem.Key key)
        {
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
                if (device is UnityEngine.InputSystem.Keyboard k && k.enabled && k[key].wasPressedThisFrame) return true;
            return false;
        }
#endif

        private void Keys()
        {
#if ENABLE_INPUT_SYSTEM
            bool enter = Down(UnityEngine.InputSystem.Key.Enter) || Down(UnityEngine.InputSystem.Key.NumpadEnter);
            bool remove = Down(UnityEngine.InputSystem.Key.Delete) || Down(UnityEngine.InputSystem.Key.Backspace);
            int dx = (Down(UnityEngine.InputSystem.Key.RightArrow) ? 1 : 0) - (Down(UnityEngine.InputSystem.Key.LeftArrow) ? 1 : 0);
            int dy = (Down(UnityEngine.InputSystem.Key.DownArrow) ? 1 : 0) - (Down(UnityEngine.InputSystem.Key.UpArrow) ? 1 : 0);
            int page = (Down(UnityEngine.InputSystem.Key.PageDown) ? 1 : 0) - (Down(UnityEngine.InputSystem.Key.PageUp) ? 1 : 0);
#else
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            bool remove = Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace);
            int dx = (Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKeyDown(KeyCode.LeftArrow) ? 1 : 0);
            int dy = (Input.GetKeyDown(KeyCode.DownArrow) ? 1 : 0) - (Input.GetKeyDown(KeyCode.UpArrow) ? 1 : 0);
            int page = (Input.GetKeyDown(KeyCode.PageDown) ? 1 : 0) - (Input.GetKeyDown(KeyCode.PageUp) ? 1 : 0);
#endif
            // Mid-drag, a change or a page would rebuild the grids under the dragged card: nothing until it's dropped.
            if (_dragging) return;
            bool hand = _handOverlay.gameObject.activeSelf;
            // The test hand or the More panel covers the cards: the keys don't act on what's hidden (Esc or B closes them).
            // The test hand's own look-only focus still moves with the arrows.
            if (hand || MoreOpen)
            {
                if (hand && !MoreOpen && _focusZone == FocusZone.Hand && (dx != 0 || dy != 0)) MoveFocus(dx, dy);
                if (page != 0 && !MoreOpen) TurnPage(page);
                return;
            }
            if (enter) { if (ShiftHeld()) KeySide(); else KeyAdd(); }
            else if (remove) KeyRemove();
            if (dx != 0 || dy != 0) MoveFocus(dx, dy);
            if (page != 0) TurnPage(page);
        }

        /// <summary>Page Up / Down, LB / RB: the next or previous page of results (never mid-drag, nor under the More panel).</summary>
        private void TurnPage(int delta)
        {
            if (_dragging || MoreOpen) return;
            _page += delta;
            ShowResults();
        }

        /// <summary>The card the keys add when no deck copy or result is focused: the one on the left.</summary>
        private CardInfo? FocusedResult() => _focusZone == FocusZone.Results ? _results.FirstOrDefault(c => Key(c.Code) == _focusBase) ?? _selected : _selected;

        /// <summary>Enter: the focused result to Main/Extra, one more copy of the focused deck card, or the card on the left.</summary>
        private void KeyAdd()
        {
            if (_focusZone == FocusZone.Hand) return;
            int slot = FocusedSlot();
            if (slot >= 0)
            {
                if (_cards.Get(Section(_deck, _focusSection)[slot]) is CardInfo info) Add(info, side: false, DeckTile(_focusSection, slot));
                return;
            }
            if (FocusedResult() is CardInfo card) Add(card, side: false);
        }

        /// <summary>Shift+Enter: the focused result to the Side Deck, or the focused deck copy between Main and Side.</summary>
        private void KeySide()
        {
            if (_focusZone == FocusZone.Hand) return;
            int slot = FocusedSlot();
            if (slot >= 0) { MoveSection(_focusSection, slot); return; }
            if (FocusedResult() is CardInfo card) Add(card, side: true);
        }

        /// <summary>Delete / Backspace: the focused deck copy out.</summary>
        private void KeyRemove()
        {
            int slot = FocusedSlot();
            if (slot >= 0) RemoveAt(_focusSection, slot);
        }

        private const int DeckPerRow = 15;

        /// <summary>The arrows: the focus moves in the grid it's in (with none, to the page's first result).</summary>
        private void MoveFocus(int dx, int dy)
        {
            if (_focusZone == FocusZone.Hand && _handOverlay.gameObject.activeSelf && _testHand != null)
            {
                FocusHand(Mathf.Clamp(_focusHand + dx + dy, 0, _testHand.Count - 1), viewed: false);
                return;
            }
            int slot = FocusedSlot();
            if (slot >= 0)
            {
                var (section, index) = DeckStep(_focusSection, slot, dx, dy);
                FocusDeck(section, index, viewed: false); // looked at by the keys: not "viewed" (only a click or the reading view is)
                return;
            }
            int first = _page * ResultsPerPage, onPage = Mathf.Clamp(_results.Count - first, 0, ResultsPerPage);
            if (onPage == 0) return;
            int at = -1;
            if (_focusZone == FocusZone.Results)
                for (int i = 0; i < onPage; i++) if (Key(_results[first + i].Code) == _focusBase) { at = i; break; }
            at = at < 0 ? 0 : Mathf.Clamp(at + dx + dy * ResultColumns, 0, onPage - 1);
            FocusResult(_results[first + at], viewed: false);
        }

        /// <summary>One step in the deck: along a section's slots, or up and down its rows and on to the next section's.</summary>
        private (DeckSection, int) DeckStep(DeckSection section, int index, int dx, int dy)
        {
            var order = new[] { DeckSection.Main, DeckSection.Extra, DeckSection.Side };
            int count = Section(_deck, section).Count;
            if (dx != 0) index = Mathf.Clamp(index + dx, 0, count - 1);
            if (dy == 0) return (section, index);
            int column = index % DeckPerRow, row = index / DeckPerRow, rows = (count + DeckPerRow - 1) / DeckPerRow;
            int next = row + dy;
            if (next >= 0 && next < rows) return (section, Mathf.Min(next * DeckPerRow + column, count - 1));
            // Off this section's rows: the next section that has cards, its nearest row.
            for (int s = System.Array.IndexOf(order, section) + dy; s >= 0 && s < order.Length; s += dy)
            {
                int n = Section(_deck, order[s]).Count;
                if (n == 0) continue;
                int r = dy > 0 ? 0 : (n - 1) / DeckPerRow;
                return (order[s], Mathf.Min(r * DeckPerRow + column, n - 1));
            }
            return (section, index);
        }

        /// <summary>
        /// The wheel scrolls the focused card's text from anywhere, except over what scrolls on its own (a list, the card text
        /// itself, the active chip row) and the More panel. Input System wheel units: one per notch (its uniform scroll
        /// setting, the default), or the platform's 120 per notch when that's off.
        /// </summary>
        private void Wheel()
        {
#if ENABLE_INPUT_SYSTEM
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device is not UnityEngine.InputSystem.Mouse mouse || !mouse.enabled) continue;
                float y = mouse.scroll.ReadValue().y;
                if (Mathf.Approximately(y, 0)) continue;
                if (SelfScrollingAt(mouse.position.ReadValue())) continue;
                _cardDetail.ScrollBy(-WheelTicks(y)); // the wheel turned down (negative): down the text
            }
            // The right stick: the pad's wheel (up the stick, up the text).
            // Only once the stick has been at rest since the editor opened: a stick that drifts past the dead zone on its own
            // never scrolls (as ControllerSupport's cursor).
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && pad.enabled)
            {
                float stick = pad.rightStick.ReadValue().y;
                if (Mathf.Abs(stick) <= StickDeadZone) _stickRested = true;
                else if (_stickRested) _cardDetail.ScrollBy(-stick * StickTicksPerSecond * Time.unscaledDeltaTime);
            }
#endif
        }

        /// <summary>Wheel ticks per second at full tilt of the right stick; its dead zone.</summary>
        private const float StickTicksPerSecond = 12f, StickDeadZone = 0.2f;

        /// <summary>The right stick has been at rest since the editor opened (see <see cref="Wheel"/>).</summary>
        private bool _stickRested;

        /// <summary>A card is being dragged (DragSource began, not yet dropped).</summary>
        private bool _dragging;

        /// <summary>Wheel notches from a mouse's scroll value (see <see cref="Wheel"/>).</summary>
        public static float WheelTicks(float raw)
        {
#if ENABLE_INPUT_SYSTEM
            bool uniform = UnityEngine.InputSystem.InputSystem.settings.scrollDeltaBehavior == UnityEngine.InputSystem.InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms;
            return uniform && Mathf.Abs(raw) < 20 ? raw : raw / 120f;
#else
            return raw;
#endif
        }

        // ---- For checks ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// For checks: where the focus is ("Results", "Deck", "Hand", "None"), its card, the tile the ring is on ("result 3",
        /// "Main 5", "hand 1" or "none"), and the card on the left.
        /// </summary>
        public (string zone, string card, string ring, string panel) DebugFocus
        {
            get
            {
                string card = _focusZone switch
                {
                    FocusZone.Results or FocusZone.Deck => _cards.Get(_focusBase)?.Name ?? "?",
                    FocusZone.Hand => _testHand != null && _focusHand < _testHand.Count ? _cards.Get(_testHand[_focusHand])?.Name ?? "?" : "?",
                    _ => "",
                };
                string ring = "none";
                if (_ring != null && _ring.transform.parent is Transform on)
                {
                    for (int i = 0; i < _resultViews.Count; i++) if (_resultViews[i].view != null && _resultViews[i].view.transform == on) ring = $"result {i}";
                    foreach (var d in _deckViews) if (d.view != null && d.view.transform == on) ring = $"{SectionName(d.section)} {d.index}";
                    for (int i = 0; i < _handViews.Count; i++) if (_handViews[i] != null && _handViews[i].transform == on) ring = $"hand {i}";
                }
                return (_focusZone.ToString(), card, ring, _selected?.Name ?? "");
            }
        }

        /// <summary>For checks: the ring's thickness, its gap and how far it reaches past the card on each side (left, right, bottom, top), or null.</summary>
        public (float width, float gap, float left, float right, float bottom, float top, string color, float alpha)? DebugRing
        {
            get
            {
                if (_ring == null || !_ring.TryGetComponent<FrameGraphic>(out var ring)) return null;
                var gap = _ring.transform.Find("Gap")?.GetComponent<FrameGraphic>();
                var rt = (RectTransform)_ring.transform;
                return (ring.Thickness, gap != null ? gap.Thickness : 0, -rt.offsetMin.x, rt.offsetMax.x, -rt.offsetMin.y, rt.offsetMax.y,
                        ColorUtility.ToHtmlStringRGB(ring.color), ring.color.a);
            }
        }

        /// <summary>For checks: whether the focus is armed (the next click on its tile acts), and where.</summary>
        public string DebugArmed => _armedAt ?? "not armed";

        /// <summary>For checks: the front of Recently viewed (the last card counted as looked at), or "".</summary>
        public string DebugViewedFront => _seenLog.Viewed.Count > 0 ? _cards.Get(_seenLog.Viewed[0])?.Name ?? "" : "";

        /// <summary>For checks: New (no question asked), the Sort button, a deck list's open state, the test hand's Close and the drag ghost.</summary>
        public void DebugNewDeck() => NewDeck();
        public void DebugSort() => Change(d => { d.Main = DeckRules.Sorted(d.Main, _cards); d.Extra = DeckRules.Sorted(d.Extra, _cards); d.Side = DeckRules.Sorted(d.Side, _cards); }, "Sort", "Sorted");
        public Vector2 DebugHandCloseOnScreen => OnScreen(_handOverlay.GetComponentsInChildren<Button>().First(b => b.GetComponentInChildren<Text>()?.text == "Close").transform);
        public bool DebugGhostShown => _ghost.gameObject.activeSelf;
        public Vector2 DebugZoneOnScreen(DeckSection s) => OnScreen(_zones.First(z => z.zone == (s == DeckSection.Main ? Zone.Main : s == DeckSection.Extra ? Zone.Extra : Zone.Side)).rect);

        /// <summary>For checks: the active buttons a shown change message overlaps (none should be).</summary>
        public List<string> DebugPillOverlaps()
        {
            var list = new List<string>();
            if (_pill == null || _pillGroup == null || _pillGroup.alpha < 0.01f) return list;
            var corners = new Vector3[4];
            Rect Of(RectTransform rt) { rt.GetWorldCorners(corners); return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y); }
            var pill = Of(_pill);
            pill = Rect.MinMaxRect(pill.xMin + 0.5f, pill.yMin + 0.5f, pill.xMax - 0.5f, pill.yMax - 0.5f);
            foreach (var b in _root.GetComponentsInChildren<Button>())
                if (!b.transform.IsChildOf(_pill) && Of((RectTransform)b.transform) is Rect r && r.Overlaps(pill))
                    list.Add(b.GetComponentInChildren<Text>()?.text ?? b.name);
            return list;
        }

        /// <summary>For checks: the toolbar's Undo, the reading view closed.</summary>
        public void DebugUndo() => Undo();
        public void DebugCloseReading() => CloseReading();
        public bool DebugReadingOpen => _reading != null;

        private static Vector2 OnScreen(Transform t)
        {
            var rt = (RectTransform)t;
            return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        }

        /// <summary>For checks: a result's centre on screen by its slot on the page, or null.</summary>
        public Vector2? DebugResultOnScreen(int slot) => slot >= 0 && slot < _resultViews.Count && _resultViews[slot].view != null ? OnScreen(_resultViews[slot].view.transform) : (Vector2?)null;

        /// <summary>For checks: a deck slot's card centre on screen (at its place, not where an animation draws it), or null.</summary>
        public Vector2? DebugDeckOnScreen(DeckSection section, int index)
        {
            foreach (var d in _deckViews)
                if (d.section == section && d.index == index && d.view != null)
                {
                    var parent = (RectTransform)d.view.transform.parent;
                    var local = parent.rect.center + d.home; // the layer's middle-anchored cards: home is from its centre
                    return RectTransformUtility.WorldToScreenPoint(null, parent.TransformPoint(local));
                }
            return null;
        }

        /// <summary>For checks: a test hand card's centre on screen, or null.</summary>
        public Vector2? DebugHandOnScreen(int i) => i >= 0 && i < _handViews.Count && _handViews[i] != null ? OnScreen(_handViews[i].transform) : (Vector2?)null;

        /// <summary>For checks: the centre on screen of a part: "detail" (the card's picture on the left), "text" (its card text), "status", "more".</summary>
        public Vector2 DebugPartOnScreen(string part) => part switch
        {
            "text" => OnScreen(_cardDetail.DebugTextView),
            "status" => OnScreen(_status.transform),
            "more" => OnScreen(_more),
            "odds" => OnScreen(_odds.transform),
            "active" => OnScreen(_activeRow),
            _ => OnScreen(_cardDetail.DebugTextView.parent.Find("Art") ?? _cardDetail.DebugTextView),
        };

        /// <summary>For checks: a deck slot's card name ("" when there's none).</summary>
        public string DebugDeckCard(DeckSection section, int index)
        {
            var list = Section(_deck, section);
            return index >= 0 && index < list.Count ? _cards.Get(list[index])?.Name ?? "" : "";
        }

        /// <summary>For checks: the names of the cards on the results page, in order.</summary>
        public List<string> DebugPageNames() => _resultViews.Select(r => _cards.Get(r.key)?.Name ?? "").ToList();

        public CardDetail DebugCardDetail => _cardDetail;

        /// <summary>
        /// For checks: focuses the first result (from the page's first on, turning pages, up to 300 cards) whose text runs on
        /// past its box; its name, or null. Leaves that page showing.
        /// </summary>
        public string? DebugFocusLongText(float minRoom = 1)
        {
            for (int i = 0; i < _results.Count && i < 300; i++)
            {
                if (i / ResultsPerPage != _page) { _page = i / ResultsPerPage; ShowResults(); }
                FocusResult(_results[i], viewed: false);
                Canvas.ForceUpdateCanvases();
                var s = _cardDetail.DebugScroll;
                if (s.runsOn && s.room >= minRoom) return _cards.Get(Key(_results[i].Code))?.Name;
            }
            return null;
        }

        /// <summary>For checks: the page of results showing (turned as Page Down / Next do).</summary>
        public void DebugPage(int page)
        {
            _page = page;
            ShowResults();
        }

        /// <summary>For checks: the editor's text fields as they are now (the Archetype box only while More is open, the save-as name while Save asks).</summary>
        public List<(string name, InputField field)> DebugTextFields()
        {
            var list = new List<(string, InputField)> { ("deck name", _nameField), ("label", _labelField), ("search", _searchField) };
            if (MoreOpen) list.Add(("archetype", _archetypeField));
            if (_savePrompt != null && _savePrompt.GetComponentInChildren<InputField>() is InputField saveAs) list.Add(("save-as name", saveAs));
            return list;
        }

        /// <summary>For checks: Save's "how to save" bar, as when Save would replace most of a deck (it holds the save-as name field).</summary>
        public void DebugAskHowToSave(bool open)
        {
            if (open) AskHowToSave(string.IsNullOrWhiteSpace(_deck.Name) ? "new deck" : _deck.Name);
            else CloseSavePrompt();
        }

        /// <summary>For checks: empties the deck (no undo step), then adds these cards to their home sections.</summary>
        public void DebugSetDeck(IEnumerable<uint> main, IEnumerable<uint> side)
        {
            _deck.Main.Clear();
            _deck.Extra.Clear();
            _deck.Side.Clear();
            foreach (var code in main)
                if (_cards.Get(code) is CardInfo info) Section(_deck, DeckRules.HomeSection(info)).Add(code);
            _deck.Side.AddRange(side);
            _undo.Clear();
            _redo.Clear();
            RefreshDeck();
        }

        private readonly List<RaycastResult> _hits = new List<RaycastResult>();

        /// <summary>Whether what's under a screen point scrolls the wheel itself, or is the More panel.</summary>
        private bool SelfScrollingAt(Vector2 screen)
        {
            var events = EventSystem.current;
            if (events == null) return false;
            _hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = screen }, _hits);
            if (_hits.Count == 0) return false;
            var top = _hits[0].gameObject.transform;
            if (MoreOpen && top.IsChildOf(_more)) return true;
            if (top.IsChildOf(_activeRow)) return true;
            var scroll = top.GetComponentInParent<ScrollRect>();
            return scroll != null && scroll.isActiveAndEnabled;
        }
    }
}
