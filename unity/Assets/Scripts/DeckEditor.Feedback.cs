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
                label.fontStyle = FontStyle.Bold;
            }
            _pillGroup = pill.gameObject.AddComponent<CanvasGroup>();
            _pillGroup.alpha = 0;
            _pill = pill;
            _pillAt = now;
        }

        /// <summary>An overlay is open over the deck (the test hand, More, the reading view, the deck list, Save's bar): no message shows or takes the pointer.</summary>
        private bool Covered => _handOverlay.gameObject.activeSelf || MoreOpen || _reading != null || _browser != null || _savePrompt != null;

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
    }
}
