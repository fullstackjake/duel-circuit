using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// What changed, shown three ways at once (docs/dev/EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md, "Showing what changed"): the
    /// card (an edge flash, a scale, the gap closing, the result's in-deck count pulsing, a shake when refused), the section's
    /// count ("(+1)" for 1.5 s), and a message at the section's header with Undo. Plus a sound under the interface volume.
    /// Everything is kept by data (the card, the section, which copy) and found again each frame, since every change rebuilds
    /// the grids.
    /// </summary>
    public sealed partial class DeckEditor
    {
        /// <summary>What a change was: one card in, out or moved; an undo or redo; anything else (Sort, Clear, Recipe...); refused.</summary>
        private enum ChangeKind { Add, Remove, Move, Undo, Redo, Bulk, Refused }

        public const float FlashHold = 0.25f, FlashFade = 0.45f, GrowFrom = 1.12f, GrowTime = 0.18f, SlideBy = 66f, SlideTime = 0.15f;
        public const float PulseFrom = 1.25f, PulseTime = 0.2f, ShakeBy = 4f, ShakeTime = 0.24f, ReducedEdge = 0.7f, DeltaTime = 1.5f;
        public const float PillIn = 0.12f, PillHold = 3.5f, PillOut = 0.4f, MergeWindow = 2f;
        private const int MaxFlashes = 5;
        private static readonly DeckSection[] Sections = { DeckSection.Main, DeckSection.Extra, DeckSection.Side };

        private sealed class Flash
        {
            public TileRef Tile;
            public Color Color;
            public float Start;
            public bool Grow;
        }

        private readonly List<Flash> _flashes = new List<Flash>();
        private (DeckSection section, int from, float start)? _slide;
        private (TileRef tile, float start)? _shake;
        private readonly Dictionary<uint, float> _pulses = new Dictionary<uint, float>();
        private readonly Dictionary<DeckSection, (int delta, float until)> _deltas = new Dictionary<DeckSection, (int, float)>();
        private readonly Dictionary<CardView, float> _offsets = new Dictionary<CardView, float>();
        private readonly HashSet<CardView> _offHome = new HashSet<CardView>();

        // The message beside the changed section's header.
        private RectTransform? _pill;
        private CanvasGroup? _pillGroup;
        private Text? _pillText;
        private Button? _pillButton;
        private FrameGraphic? _pillEdge;
        private float _pillAt;
        private (ChangeKind kind, uint key, DeckSection section, int count, float at) _pillLast;
        /// <summary>How many steps the message's button takes back (or does again): a merged "×3" is three.</summary>
        private int _pillSteps = 1;
        /// <summary>The messages' layer: over the deck, under every overlay (the test hand, More, the reading view, the deck list, Save's bar).</summary>
        private RectTransform _pillLayer = null!;

        private readonly List<string> _soundLog = new List<string>();

        // ---- The changes ------------------------------------------------------------------------------------------------

        /// <summary>The single-slot edits between two decks (per section: one slot added or one taken out); null when it's more than that.</summary>
        private static List<(DeckSection section, int index, int sign, uint code)>? Edits(Deck before, Deck after)
        {
            var edits = new List<(DeckSection, int, int, uint)>();
            foreach (var s in Sections)
            {
                var a = Section(before, s);
                var b = Section(after, s);
                if (a.SequenceEqual(b)) continue;
                int i = 0, n = Math.Min(a.Count, b.Count);
                while (i < n && a[i] == b[i]) i++;
                if (b.Count == a.Count + 1 && a.Skip(i).SequenceEqual(b.Skip(i + 1))) edits.Add((s, i, +1, b[i]));
                else if (b.Count == a.Count - 1 && a.Skip(i + 1).SequenceEqual(b.Skip(i))) edits.Add((s, i, -1, a[i]));
                else return null;
            }
            return edits;
        }

        /// <summary>
        /// A change made (the deck was <paramref name="before"/>): the card's signal from what changed between the decks (none
        /// past a single add, removal or move: then only the counts), the section counts, the message and the sound.
        /// </summary>
        private void Signal(Deck before, ChangeKind kind, string words, DeckSection section, uint key = 0, int steps = 1)
        {
            var T = UiTheme.Current;
            var edits = Edits(before, _deck);
            if (edits != null)
            {
                var adds = edits.Where(e => e.sign > 0).ToList();
                var takes = edits.Where(e => e.sign < 0).ToList();
                if (adds.Count == 1 && takes.Count == 0)
                {
                    var e = adds[0];
                    FlashTile(new TileRef(true, e.section, Key(e.code), CopyOrdinal(Section(_deck, e.section), e.index)), T.Good, grow: true);
                    Pulse(Key(e.code));
                }
                else if (takes.Count == 1 && adds.Count == 0)
                {
                    var e = takes[0];
                    if (!FxSettings.ReducedMotion) _slide = (e.section, e.index, Time.unscaledTime);
                    FlashTile(TileRef.Result(Key(e.code)), T.Bad, grow: false);
                    Pulse(Key(e.code));
                }
                else if (adds.Count == 1 && takes.Count == 1 && Key(adds[0].code) == Key(takes[0].code))
                {
                    var e = adds[0];
                    FlashTile(new TileRef(true, e.section, Key(e.code), CopyOrdinal(Section(_deck, e.section), e.index)), T.Text, grow: false);
                }
            }
            foreach (var s in Sections)
            {
                int d = Section(_deck, s).Count - Section(before, s).Count;
                if (d != 0) AddDelta(s, d);
            }
            ShowHeaders();
            ShowPill(kind, words, section, key, steps);
            switch (kind)
            {
                case ChangeKind.Add: Sound("place", 0.35f); break;
                case ChangeKind.Remove: Sound("draw", 0.30f, 0.9f); break;
                case ChangeKind.Move: Sound("move", 0.30f); break;
                case ChangeKind.Undo: Sound("move", 0.30f, 0.85f); break;
                case ChangeKind.Redo: Sound("move", 0.30f, 1.1f); break;
                case ChangeKind.Bulk: Sound("move", 0.30f); break;
            }
        }

        /// <summary>A change that can't be made: the tile shakes (reduced motion: a red edge), a red message with no Undo at the target section, the error sound. The deck stays as it is.</summary>
        private void Refuse(string words, DeckSection section, TileRef tile)
        {
            if (FxSettings.ReducedMotion) FlashTile(tile, UiTheme.Current.Bad, grow: false);
            else _shake = (tile, Time.unscaledTime);
            ShowPill(ChangeKind.Refused, words, section, 0);
            Sound("error", 0.50f);
        }

        /// <summary>The section a card goes to or comes from, by name ("Main", "Extra", "Side").</summary>
        private static string SectionName(DeckSection s) => s == DeckSection.Main ? "Main" : s == DeckSection.Extra ? "Extra" : "Side";

        private void FlashTile(TileRef tile, Color color, bool grow)
        {
            _flashes.RemoveAll(f => SameTile(f.Tile, tile));
            if (_flashes.Count >= MaxFlashes) return; // past that, only the counts signal
            _flashes.Add(new Flash { Tile = tile, Color = color, Start = Time.unscaledTime, Grow = grow });
        }

        private static bool SameTile(TileRef a, TileRef b) => a.Deck == b.Deck && a.Key == b.Key && (!a.Deck || (a.Section == b.Section && a.Copy == b.Copy));

        private void Pulse(uint key)
        {
            if (!FxSettings.ReducedMotion) _pulses[key] = Time.unscaledTime;
        }

        private void AddDelta(DeckSection s, int delta)
        {
            float now = Time.unscaledTime;
            if (_deltas.TryGetValue(s, out var old) && old.until > now && Math.Sign(old.delta) == Math.Sign(delta)) delta += old.delta;
            _deltas[s] = (delta, now + DeltaTime);
        }

        /// <summary>The section headers: "Main 40" and what's in it, with "(+1)" or "(−1)" for 1.5 s after a change.</summary>
        private void ShowHeaders()
        {
            var T = UiTheme.Current;
            float now = Time.unscaledTime;
            string Head(DeckSection s, string name, int n)
            {
                var head = $"<b>{name} {n}</b>";
                if (_deltas.TryGetValue(s, out var d) && d.until > now && d.delta != 0)
                    head += $" <color={(d.delta > 0 ? T.GoodHex : T.BadHex)}><b>({(d.delta > 0 ? "+" : "−")}{Math.Abs(d.delta)})</b></color>";
                return head;
            }
            _mainHeader.text = $"{Head(DeckSection.Main, "Main", _deck.Main.Count)}   {DeckRules.Breakdown(_deck.Main, _cards)}";
            _extraHeader.text = $"{Head(DeckSection.Extra, "Extra", _deck.Extra.Count)}   {DeckRules.ExtraBreakdown(_deck.Extra, _cards)}";
            _sideHeader.text = $"{Head(DeckSection.Side, "Side", _deck.Side.Count)}   {DeckRules.Breakdown(_deck.Side, _cards)}";
            foreach (var t in new[] { _mainHeader, _extraHeader, _sideHeader }) t.supportRichText = true;
        }

        private void Sound(string name, float volume, float pitch = 1f)
        {
            Sounds.Play(name, volume, pitch, ui: true);
            var line = string.Format(CultureInfo.InvariantCulture, "{0} {1:0.00} pitch {2:0.00} (interface volume)", name, volume, pitch);
            if (!DuelController.IsCheckRun) return; // the log is for checks (they run muted)
            _soundLog.Add(line);
            Debug.Log($"editor sound: {line}");
        }

        // ---- The message beside the section's header ----------------------------------------------------------------------

        private static float HeaderTop(DeckSection s) => s == DeckSection.Main ? 126 : s == DeckSection.Extra ? 538 : 662;

        private static Color KindColor(ChangeKind kind)
        {
            var T = UiTheme.Current;
            return kind switch
            {
                ChangeKind.Add => T.Good,
                ChangeKind.Move => T.Text,
                ChangeKind.Undo or ChangeKind.Redo or ChangeKind.Bulk => T.Info,
                _ => T.Bad,
            };
        }

        /// <summary>
        /// The message: "+ Pot of Greed", "− Pot of Greed", "Pot of Greed → Side", "Sorted", "Cleared 40 cards", "Undone: + Pot
        /// of Greed" (with Redo), a refusal (red, no button). The same card, kind and section (so the same direction) within
        /// 2 s merge ("+ Pot of Greed ×2", whose Undo takes both back); anything else replaces it. It lives under every
        /// overlay and hides while one is open (<see cref="Covered"/>).
        /// </summary>
        private void ShowPill(ChangeKind kind, string words, DeckSection section, uint key, int steps = 1)
        {
            var T = UiTheme.Current;
            float now = Time.unscaledTime;
            bool merges = _pill != null && key != 0 && kind is ChangeKind.Add or ChangeKind.Remove or ChangeKind.Move
                          && _pillLast.kind == kind && _pillLast.key == key && _pillLast.section == section && now - _pillLast.at <= MergeWindow;
            int count = merges ? _pillLast.count + 1 : 1;
            _pillLast = (kind, key, section, count, now);
            _pillSteps = merges ? count : steps;
            var text = count > 1 ? $"{words} ×{count}" : words;
            if (merges && _pillText != null)
            {
                _pillText.text = text;
                _pillAt = now - PillIn; // held again from now, already in
                return;
            }
            if (_pill != null) Destroy(_pill.gameObject);
            var pill = Box(_pillLayer, 1094, HeaderTop(section) - 1, 296, 24);
            pill.name = "ChangePill";
            Ui.Image(pill, T.PanelDeep.WithAlpha(0.95f)).raycastTarget = true; // takes the pointer: pointed at, it stays
            _pillEdge = FrameGraphic.Make("Edge", pill, 0, 1, KindColor(kind));
            bool button = kind != ChangeKind.Refused;
            _pillText = Ui.Text(Box(pill, 8, 0, button ? 296 - 8 - 62 : 296 - 16, 24), text, 15, T.Text, TextAnchor.MiddleLeft);
            _pillText.resizeTextForBestFit = true;
            _pillText.resizeTextMinSize = 11;
            _pillText.resizeTextMaxSize = 15;
            _pillButton = null;
            if (button)
            {
                bool redo = kind == ChangeKind.Undo;
                _pillButton = Button(296 - 58, 2, 56, 20, redo ? "Redo" : "Undo", () => { if (redo) RedoSteps(_pillSteps); else UndoSteps(_pillSteps); }, pill);
                var label = _pillButton.GetComponentInChildren<Text>();
                label.color = T.Info;
                label.fontSize = 14;
                Typography.Strong(label);
            }
            _pillGroup = pill.gameObject.AddComponent<CanvasGroup>();
            _pillGroup.alpha = 0;
            _pill = pill;
            _pillAt = now;
        }

        /// <summary>An overlay is open over the deck (the test hand, More, the reading view, the deck list, Save's bar, the leave question): no message shows or takes the pointer.</summary>
        private bool Covered => _handOverlay.gameObject.activeSelf || MoreOpen || _reading != null || _browser != null || _savePrompt != null || _leaveDialog != null;

        private readonly List<UnityEngine.EventSystems.RaycastResult> _pillHits = new List<UnityEngine.EventSystems.RaycastResult>();

        /// <summary>The pointer is on the message itself (the top thing under it), so it stays open.</summary>
        private bool PointerOver(RectTransform rt)
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return false;
#if ENABLE_INPUT_SYSTEM
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device is not UnityEngine.InputSystem.Mouse m || !m.enabled) continue;
                _pillHits.Clear();
                events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = m.position.ReadValue() }, _pillHits);
                if (_pillHits.Count > 0 && _pillHits[0].gameObject.transform.IsChildOf(rt)) return true;
            }
#endif
            return false;
        }

        /// <summary>No message, counts, edges or motion left (another deck, the editor closed).</summary>
        private void ClearFeedback()
        {
            if (_pill != null) Destroy(_pill.gameObject);
            _pill = null;
            _pillGroup = null;
            _pillText = null;
            _pillButton = null;
            _pillLast = default;
            _pillSteps = 1;
            _deltas.Clear();
            _flashes.Clear();
            _pulses.Clear();
            _slide = null;
            _shake = null;
            foreach (var d in _deckViews)
                if (d.view != null) { d.view.transform.localScale = Vector3.one; ((RectTransform)d.view.transform).anchoredPosition = d.home; if (d.view.transform.Find("ChangeEdge") is Transform e) Destroy(e.gameObject); }
            foreach (var r in _resultViews)
                if (r.view != null) { r.view.transform.localScale = Vector3.one; ((RectTransform)r.view.transform).anchoredPosition = r.home; if (r.view.transform.Find("ChangeEdge") is Transform e) Destroy(e.gameObject); if (r.view.transform.Find("InDeck") is Transform c) c.localScale = Vector3.one; }
            _offHome.Clear();
            if (_mainHeader != null) ShowHeaders();
        }

        // ---- Each frame ---------------------------------------------------------------------------------------------------

        private CardView? ViewOf(TileRef tile)
        {
            if (tile.Deck)
            {
                int slot = SlotOf(tile.Section, tile.Key, tile.Copy);
                foreach (var d in _deckViews) if (d.section == tile.Section && d.index == slot) return d.view;
                return null;
            }
            foreach (var r in _resultViews) if (r.key == tile.Key) return r.view;
            return null;
        }

        private static float EaseOut(float t) => 1 - (1 - t) * (1 - t);

        /// <summary>The signals' frames: edges, scales, the slide, the shake, the count pulse, the header counts, the message.</summary>
        private void AnimateFeedback()
        {
            if (_flashes.Count == 0 && _slide == null && _shake == null && _pulses.Count == 0 && _deltas.Count == 0 && _pill == null && _offHome.Count == 0) return;
            float now = Time.unscaledTime;
            bool reduced = FxSettings.ReducedMotion;

            // Where the tiles are drawn: the cards after a removal slide in to close the gap; a refused one shakes.
            _offsets.Clear();
            if (_slide is { } slide)
            {
                float t = (now - slide.start) / SlideTime;
                if (t >= 1 || reduced) _slide = null;
                else foreach (var d in _deckViews) if (d.section == slide.section && d.index >= slide.from && d.view != null) _offsets[d.view] = SlideBy * (1 - EaseOut(t));
            }
            if (_shake is { } shake)
            {
                float t = (now - shake.start) / ShakeTime;
                if (t >= 1 || reduced) _shake = null;
                else if (ViewOf(shake.tile) is CardView v) _offsets[v] = (_offsets.TryGetValue(v, out var x) ? x : 0) + ShakeBy * Mathf.Sin(t * 3 * 2 * Mathf.PI);
            }
            foreach (var d in _deckViews) Place(d.view, d.home);
            foreach (var r in _resultViews) Place(r.view, r.home);
            void Place(CardView view, Vector2 home)
            {
                if (view == null) return;
                bool off = _offsets.TryGetValue(view, out var dx);
                if (!off && !_offHome.Contains(view)) return;
                ((RectTransform)view.transform).anchoredPosition = home + new Vector2(off ? dx : 0, 0);
                if (off) _offHome.Add(view);
                else _offHome.Remove(view);
            }
            _offHome.RemoveWhere(v => v == null);

            // Edges and the added card's scale.
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var f = _flashes[i];
                float t = now - f.Start;
                bool done = t >= (reduced ? ReducedEdge : FlashHold + FlashFade);
                float alpha = reduced ? 1 : t < FlashHold ? 1 : 1 - (t - FlashHold) / FlashFade;
                var view = ViewOf(f.Tile);
                if (view != null)
                {
                    var edge = view.transform.Find("ChangeEdge")?.GetComponent<FrameGraphic>();
                    if (edge == null && !done)
                    {
                        edge = FrameGraphic.Make("ChangeEdge", view.transform, 0, 3, f.Color);
                        if (view.TryGetComponent<UiHover>(out var hover)) hover.Adopt(edge.gameObject);
                    }
                    if (edge != null)
                    {
                        if (done) Destroy(edge.gameObject);
                        else edge.color = f.Color.WithAlpha(Mathf.Clamp01(alpha));
                    }
                    if (f.Grow) view.transform.localScale = Vector3.one * (reduced || done || t >= GrowTime ? 1 : Mathf.Lerp(GrowFrom, 1, EaseOut(t / GrowTime)));
                }
                if (done) _flashes.RemoveAt(i);
            }

            // A result's in-deck count pulses when it changes.
            if (_pulses.Count > 0)
                foreach (var key in _pulses.Keys.ToList())
                {
                    float t = (now - _pulses[key]) / PulseTime;
                    bool done = t >= 1 || reduced;
                    foreach (var r in _resultViews)
                        if (r.key == key && r.view != null && r.view.transform.Find("InDeck") is Transform count)
                            count.localScale = Vector3.one * (done ? 1 : Mathf.Lerp(PulseFrom, 1, t));
                    if (done) _pulses.Remove(key);
                }

            // "(+1)" goes after 1.5 s.
            if (_deltas.Count > 0 && _deltas.Any(d => d.Value.until <= now))
            {
                foreach (var s in _deltas.Where(d => d.Value.until <= now).Select(d => d.Key).ToList()) _deltas.Remove(s);
                ShowHeaders();
            }

            // The message: in over 0.12 s, held 3.5 s (and while pointed at), out over 0.4 s.
            if (_pill != null && _pillGroup != null)
            {
                bool covered = Covered;
                float t = now - _pillAt;
                if (!covered && t > PillIn && t < PillIn + PillHold + PillOut && PointerOver(_pill)) { _pillAt = now - PillIn; t = PillIn; }
                float a = t < PillIn ? t / PillIn : t < PillIn + PillHold ? 1 : 1 - (t - PillIn - PillHold) / PillOut;
                if (a <= 0 && t > PillIn) { Destroy(_pill.gameObject); _pill = null; _pillGroup = null; _pillText = null; _pillButton = null; }
                else
                {
                    // Under an overlay it neither shows nor takes a click (its time runs on).
                    _pillGroup.alpha = covered ? 0 : Mathf.Clamp01(a);
                    _pillGroup.blocksRaycasts = _pillGroup.interactable = !covered;
                }
            }
        }

        // ---- For checks -----------------------------------------------------------------------------------------------------

        /// <summary>For checks: the message showing (its words, edge colour, button, opacity, the section it's beside), or null.</summary>
        public (string text, string edge, string? button, float alpha, string section)? DebugPill
        {
            get
            {
                if (_pill == null || _pillText == null || _pillGroup == null) return null;
                float top = -((RectTransform)_pill).anchoredPosition.y + 1;
                string section = Mathf.Approximately(top, 126) ? "Main" : Mathf.Approximately(top, 538) ? "Extra" : Mathf.Approximately(top, 662) ? "Side" : top.ToString("0");
                return (_pillText.text, _pillEdge != null ? ColorUtility.ToHtmlStringRGB(_pillEdge.color) : "", _pillButton != null ? _pillButton.GetComponentInChildren<Text>().text : null,
                        _pillGroup.alpha, section);
            }
        }

        /// <summary>For checks: the message's button, centre on screen (null without one).</summary>
        public Vector2? DebugPillButtonOnScreen =>
            _pillButton == null ? (Vector2?)null : RectTransformUtility.WorldToScreenPoint(null, _pillButton.transform.TransformPoint(((RectTransform)_pillButton.transform).rect.center));

        /// <summary>For checks: a section header's words without markup ("Main 41 (+1)   ...").</summary>
        public string DebugHeader(DeckSection s) =>
            System.Text.RegularExpressions.Regex.Replace((s == DeckSection.Main ? _mainHeader : s == DeckSection.Extra ? _extraHeader : _sideHeader).text, "<.*?>", "");

        /// <summary>For checks: the changed-card edges drawn now: where (a deck slot or a result), the colour, opacity and the tile's scale.</summary>
        public List<(string where, string color, float alpha, float scale)> DebugEdges()
        {
            var list = new List<(string, string, float, float)>();
            foreach (var d in _deckViews)
                if (d.view != null && d.view.transform.Find("ChangeEdge")?.GetComponent<FrameGraphic>() is FrameGraphic e)
                    list.Add(($"{SectionName(d.section)} {d.index}", ColorUtility.ToHtmlStringRGB(e.color), e.color.a, d.view.transform.localScale.x));
            foreach (var r in _resultViews)
                if (r.view != null && r.view.transform.Find("ChangeEdge")?.GetComponent<FrameGraphic>() is FrameGraphic e)
                    list.Add(($"result {_cards.Get(r.key)?.Name}", ColorUtility.ToHtmlStringRGB(e.color), e.color.a, r.view.transform.localScale.x));
            return list;
        }

        /// <summary>For checks: how far a deck slot's card is drawn from its place (x), and its scale.</summary>
        public (float dx, float scale) DebugDeckTile(DeckSection s, int index)
        {
            foreach (var d in _deckViews)
                if (d.section == s && d.index == index && d.view != null)
                    return (((RectTransform)d.view.transform).anchoredPosition.x - d.home.x, d.view.transform.localScale.x);
            return (float.NaN, float.NaN);
        }

        /// <summary>For checks: how far a result's card is drawn from its place (x), and its in-deck count's scale (0 without one).</summary>
        public (float dx, float countScale) DebugResultTile(string name)
        {
            foreach (var r in _resultViews)
                if (r.view != null && _cards.Get(r.key)?.Name == name)
                    return (((RectTransform)r.view.transform).anchoredPosition.x - r.home.x, r.view.transform.Find("InDeck") is Transform c ? c.localScale.x : 0);
            return (float.NaN, float.NaN);
        }

        /// <summary>For checks: the sounds the editor asked for (they're never heard: checks run muted).</summary>
        public List<string> DebugSounds => _soundLog.ToList();
        public void DebugClearSounds() => _soundLog.Clear();

        /// <summary>For checks: the message, counts and edges let go (as if they'd run out).</summary>
        public void DebugClearSignals()
        {
            ClearFeedback();
            ShowResults();
            RefreshDeck();
        }

        /// <summary>For checks: the message's box (1920x1080 layout from the top left) and whether it shows and takes the pointer now.</summary>
        public (Rect box, bool shown, bool takesPointer)? DebugPillBox =>
            _pill == null || _pillGroup == null ? null
                : (new Rect(((RectTransform)_pill).anchoredPosition.x, -((RectTransform)_pill).anchoredPosition.y, 296, 24), _pillGroup.alpha > 0.01f, _pillGroup.blocksRaycasts);
            // ---- Finding what's illegal (docs/dev/EDITOR-QUICK-FIXES-SPEC-2026-11-20.md part E) ------------------------------

        /// <summary>The red of a card a problem names (the Forbidden X's).</summary>
        private static readonly Color ProblemRed = new Color32(0xD9, 0x43, 0x3F, 0xFF);

        /// <summary>The base cards the deck's problems name (set by RefreshDeck before the grids are drawn).</summary>
        private readonly HashSet<uint> _problemKeys = new HashSet<uint>();

        /// <summary>A problem card's badge: its limit when it's over a limit of 1 or 2, X when Forbidden, ! for anything else.</summary>
        private string ProblemBadge(uint key)
        {
            int allowed = EffectiveList.Allowed(key, _cards);
            if (allowed == 0) return "X";
            int copies = _deck.Main.Concat(_deck.Extra).Concat(_deck.Side).Count(c => Key(c) == key);
            return allowed is 1 or 2 && copies > allowed ? allowed.ToString() : "!";
        }

        /// <summary>Over the problems box: one target per shown problem line that names a card (its base card, the target, its underline).</summary>
        private RectTransform _problemLinks = null!;
        private readonly List<(uint key, RectTransform target, GameObject underline, int line)> _links = new List<(uint, RectTransform, GameObject, int)>();
        /// <summary>What the links were last built from, and the screen they were measured on (rebuilt when it changes).</summary>
        private (List<DeckProblem> shown, int firstLine, Vector2Int screen) _linksFrom = (new List<DeckProblem>(), 0, Vector2Int.zero);
        private bool _linksStale;
        private UnityEngine.EventSystems.PointerEventData? _linkPointer;
        private UnityEngine.EventSystems.EventSystem? _linkPointerFor;
        private Vector2 _linkPointerAt = new Vector2(float.NaN, float.NaN);
        private RectTransform? _linkOver;

        /// <summary>
        /// The shown problem lines that name a card become links: a target the size of the line as drawn (wrapped lines too)
        /// over the unchanged block, underlined while pointed at; a click focuses that card's first copy in the deck.
        /// <paramref name="firstLine"/>: the block's line the problems start on (after the story, Ban Passes and analysis).
        /// </summary>
        private void ShowProblemLinks(List<DeckProblem> shown, int firstLine)
        {
            var T = UiTheme.Current;
            ClearChildren(_problemLinks);
            _links.Clear();
            _linksFrom = (shown, firstLine, new Vector2Int(Screen.width, Screen.height));
            _linksStale = false;
            _linkOver = null;
            _linkPointerAt = new Vector2(float.NaN, float.NaN); // look again for the line under the pointer
            if (shown.Count == 0 || !shown.Any(p => p.Code != 0)) return;
            var lines = _problems.text.Split('\n');
            var size = _problems.rectTransform.rect.size;
            var settings = _problems.GetGenerationSettings(size);
            settings.verticalOverflow = VerticalWrapMode.Overflow; // the lines' own heights, cut off or not
            var gen = _problems.cachedTextGeneratorForLayout;
            float ppu = _problems.pixelsPerUnit > 0 ? _problems.pixelsPerUnit : 1;
            float Height(string s) => gen.GetPreferredHeight(s, settings) / ppu;
            float Width(string s) => gen.GetPreferredWidth(s, settings) / ppu;
            for (int k = 0; k < shown.Count; k++)
            {
                int at = firstLine + k;
                if (shown[k].Code == 0 || at >= lines.Length) continue;
                float bottom = Height(string.Join("\n", lines.Take(at + 1)));
                float own = Height(lines[at]);
                float top = bottom - own;
                if (top >= size.y - 1) break; // cut off by the box: not drawn, so no link
                bottom = Mathf.Min(bottom, size.y);
                var target = Box(_problemLinks, 0, top, size.x, bottom - top);
                target.name = "ProblemLink";
                var key = Key(shown[k].Code);
                Ui.Clickable(target, () => FocusProblem(key));
                // The underline: 2 px (a line at least one screen pixel at 1280x720), under the line's last row.
                float width = Mathf.Min(size.x, Width(System.Text.RegularExpressions.Regex.Replace(lines[at], "<.*?>", "")) + 2);
                var line = Box(target, 0, bottom - top - 2, own > Height("A") * 1.5f ? size.x : width, 2);
                Ui.Image(line, T.Text.WithAlpha(0.8f)).raycastTarget = false;
                line.gameObject.SetActive(false);
                _links.Add((key, target, line.gameObject, at));
            }
        }

        /// <summary>A click on a problem line: that card's first copy in the deck (Main, then Extra, then Side) is focused, not armed.</summary>
        private void FocusProblem(uint key)
        {
            foreach (var section in Sections)
            {
                int index = Section(_deck, section).FindIndex(c => Key(c) == key);
                if (index < 0) continue;
                FocusDeck(section, index, viewed: false);
                return;
            }
        }

        private readonly List<UnityEngine.EventSystems.RaycastResult> _linkHits = new List<UnityEngine.EventSystems.RaycastResult>();

        /// <summary>The problem line under the pointer (the top thing there) is underlined; the others aren't.</summary>
        private void HoverProblemLinks()
        {
            // A new window size wraps the text anew: the targets are measured again, a frame after the canvas has the size.
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (_linksStale) ShowProblemLinks(_linksFrom.shown, _linksFrom.firstLine);
            else if (screen != _linksFrom.screen && _linksFrom.shown.Count > 0) _linksStale = true;
            if (_links.Count == 0) return;
            RectTransform? over = null;
            var events = UnityEngine.EventSystems.EventSystem.current;
#if ENABLE_INPUT_SYSTEM
            if (events != null && !Covered)
            {
                bool any = false;
                Vector2 at = default;
                foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
                    if (device is UnityEngine.InputSystem.Mouse m && m.enabled) { at = m.position.ReadValue(); any = true; }
                if (!any) over = null;
                else if (at == _linkPointerAt) over = _linkOver; // the pointer hasn't moved: no new raycast
                else
                {
                    _linkPointerAt = at;
                    if (_linkPointer == null || _linkPointerFor != events) { _linkPointer = new UnityEngine.EventSystems.PointerEventData(events); _linkPointerFor = events; }
                    _linkPointer.position = at;
                    _linkHits.Clear();
                    events.RaycastAll(_linkPointer, _linkHits);
                    if (_linkHits.Count > 0 && _linkHits[0].gameObject.transform.parent == _problemLinks) over = (RectTransform)_linkHits[0].gameObject.transform;
                }
            }
            else _linkPointerAt = new Vector2(float.NaN, float.NaN); // covered: look again once uncovered
#endif
            _linkOver = over;
            foreach (var (_, target, underline, _) in _links)
                if (underline != null && underline.activeSelf != (target == over)) underline.SetActive(target == over);
        }

        /// <summary>For checks: the lines of the problems box (by index in its text) that are links.</summary>
        public List<int> DebugProblemLinkLines() => _links.Select(l => l.line).ToList();

        /// <summary>For checks: what a window of another size would do: the targets are measured again.</summary>
        public void DebugRemeasureLinks() => _linksStale = true;

        /// <summary>For checks: the problems box as shown, rich text included.</summary>
        public string DebugProblemsText => _problems.text;

        /// <summary>For checks: every badge in the deck's grids (limit, X, !, P): where, its text and its colour.</summary>
        public IReadOnlyList<(DeckSection section, int index, string text, string color)> DebugProblemBadges()
        {
            var list = new List<(DeckSection, int, string, string)>();
            foreach (var d in _deckViews)
                if (d.view != null && d.view.transform.Find("Limit") is Transform badge && badge.GetComponent<Image>() is Image image)
                    list.Add((d.section, d.index, badge.GetComponentInChildren<Text>()?.text ?? "", ColorUtility.ToHtmlStringRGB(image.color)));
            return list;
        }

        /// <summary>For checks: clicks that card's problem line (as a click does); false when it has none.</summary>
        public bool DebugClickProblem(uint code)
        {
            var key = code == 0 ? 0 : Key(code);
            foreach (var (k, target, _, _) in _links)
                if (k == key && target != null && target.GetComponent<Clickable>() is Clickable c) { c.Left?.Invoke(); return true; }
            return false;
        }

        /// <summary>For checks: the problem links (their card, and whether underlined), and one's centre on screen.</summary>
        public List<(string card, bool underlined)> DebugProblemLinks() =>
            _links.Select(l => (_cards.Get(l.key)?.Name ?? l.key.ToString(), l.underline != null && l.underline.activeSelf)).ToList();
        public Vector2? DebugProblemLinkOnScreen(uint code)
        {
            var key = Key(code);
            foreach (var (k, target, _, _) in _links)
                if (k == key && target != null) return RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));
            return null;
        }
    }
}
