using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Interactive;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The play layout, used for every duel unless the developer panel is on: the board fills the screen, the card
    /// detail slides in only while you look at a card (or the duel shows one), a slim top bar holds Menu, the duel's
    /// title, History and a settings gear, and while watching a small bar of replay controls floats bottom right.
    /// </summary>
    public sealed partial class BoardView
    {
        private Transform _canvas = null!;
        private RectTransform _detailGroup = null!, _side2 = null!;
        private CanvasGroup _detailFade = null!;
        private RectTransform _controlsRt = null!;
        private Image _watchBg = null!;
        private Text _headerTitle = null!;
        private GameObject _headerButtons = null!;
        private RectTransform _gearMenu = null!;
        private bool _detailOnLeft;
        private float _detailShownUntil, _pointerSeenAt = -10f, _detailSlide;

        /// <summary>Opened from the gear: Settings, and the developer panel (F12).</summary>
        public event Action? SettingsClicked, DevPanelClicked;

        /// <summary>For checks: the gear's Settings row, as if clicked.</summary>
        public void DebugOpenSettings() => SettingsClicked?.Invoke();

        private const float ColumnWidth = 310f / 1920f;

        private void BuildPlayChrome(Transform canvas, RectTransform side, RectTransform infoBg, RectTransform infoRt, RectTransform second)
        {
            _canvas = canvas;
            _side2 = side;
            // The card detail (and the pinned compare panel) in a group of its own that can slide in and out.
            _detailGroup = Ui.Stretched("Detail", canvas, side.anchorMin, side.anchorMax);
            _detailFade = _detailGroup.gameObject.AddComponent<CanvasGroup>();
            _detailGroup.SetSiblingIndex(side.GetSiblingIndex() + 1);
            foreach (var child in new[] { infoBg, infoRt, second }) child.SetParent(_detailGroup, false);
            UnblockPanelBackgrounds();

            // Watching: the controls float on the mat with a backing of their own.
            _watchBg = Ui.Image(Ui.Stretched("WatchBg", _controlsRt, Vector2.zero, Vector2.one), new Color(0.05f, 0.07f, 0.11f, 0.88f));
            _watchBg.raycastTarget = false;
            _watchBg.transform.SetAsFirstSibling();

            // The top bar: Menu on the left, then the duel's title; History and the gear on the right.
            var menu = _headerRt.Find("Menu") as RectTransform;
            if (menu != null) { menu.anchorMin = menu.anchorMax = new Vector2(0, 0.5f); menu.pivot = new Vector2(0, 0.5f); menu.anchoredPosition = new Vector2(12, 0); }
            var title = Ui.Rect("Title", _headerRt);
            title.anchorMin = title.anchorMax = new Vector2(0, 0.5f);
            title.pivot = new Vector2(0, 0.5f);
            title.anchoredPosition = new Vector2(70, 0);
            title.sizeDelta = new Vector2(520, 40);
            // Opaque gold with a dark edge: there's no bar behind it, and it must read over the brightest mats.
            _headerTitle = Ui.Text(title, "", 16, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft);
            _headerTitle.supportRichText = true;
            var titleEdge = _headerTitle.gameObject.AddComponent<Outline>();
            titleEdge.effectColor = new Color(0, 0, 0, 0.8f);
            titleEdge.effectDistance = new Vector2(1, -1);
            var buttons = Ui.Rect("Buttons", _headerRt);
            buttons.anchorMin = buttons.anchorMax = new Vector2(1, 0.5f);
            buttons.pivot = new Vector2(1, 0.5f);
            buttons.anchoredPosition = new Vector2(-12, 0);
            buttons.sizeDelta = new Vector2(110, 40);
            _headerButtons = buttons.gameObject;
            // Corner icons: History (a list) and the gear, small, over the mat.
            HeaderButton(buttons, "history", "", new Vector2(-28, 0), new Vector2(46, 38), ToggleHistory);
            HeaderButton(buttons, "gear", "", new Vector2(28, 0), new Vector2(46, 38), ToggleGear);

            _gearMenu = Ui.Rect("Gear", canvas);
            _gearMenu.anchorMin = _gearMenu.anchorMax = new Vector2(1, 1);
            _gearMenu.pivot = new Vector2(1, 1);
            _gearMenu.anchoredPosition = new Vector2(-12, -54);
            _gearMenu.gameObject.SetActive(false);
            BuildHistory();
        }

        private static void HeaderButton(RectTransform parent, string icon, string label, Vector2 at, Vector2 size, Action click)
        {
            var b = Ui.Button(parent, label, at, size, click);
            var rt = (RectTransform)b.transform;
            if (icon is "gear" or "history")
            {
                var mark = Ui.Placed(icon == "gear" ? "Gear" : "List", rt, Vector2.zero, new Vector2(24, 24)).gameObject.AddComponent<RawImage>();
                mark.texture = Icons.For(icon);
                mark.raycastTarget = false;
            }
        }

        /// <summary>Applies the play layout's pieces (called from ApplyLayout).</summary>
        private void ApplyPlayChrome()
        {
            if (_detailGroup == null) return;
            _headerButtons.SetActive(_playLayout);
            _headerTitle.gameObject.SetActive(_playLayout);
            // The play layout has no bar: the mat shows through, with icons in the corners; the turn is on the ring.
            _headerRt.GetComponent<Image>().color = _playLayout ? new Color(0, 0, 0, 0) : Ui.Hex("#0F1117");
            _header.gameObject.SetActive(!_playLayout);
            _headerTitle.text = _campaignTitle.text;
            if (!_playLayout) { _gearMenu.gameObject.SetActive(false); if (_historyOpen) ToggleHistory(); }
            _logButton.SetActive(false);

            bool watching = !_humanMode;
            if (!_playLayout)
            {
                _logButton.SetActive(false);
                _controlsRt.SetParent(_side2, false);
                _controlsRt.anchorMin = Vector2.zero;
                _controlsRt.anchorMax = new Vector2(1, 0.1f);
                _controlsRt.offsetMin = _controlsRt.offsetMax = Vector2.zero;
                _controlsRt.pivot = new Vector2(0.5f, 0.5f);
                _watchBg.enabled = false;
                _controlsRt.gameObject.SetActive(true);
                _speedGroup.anchoredPosition = Vector2.zero;
                ((RectTransform)_spectatorButtons.transform).anchoredPosition = Vector2.zero;
            }
            else if (watching)
            {
                // Replays and duels between characters: Pause, Step, speed (and the replay row) bottom right.
                _controlsRt.SetParent(_canvas, false);
                _controlsRt.anchorMin = _controlsRt.anchorMax = new Vector2(1, 0);
                _controlsRt.pivot = new Vector2(1, 0);
                _controlsRt.sizeDelta = new Vector2(560, _replayMode ? 112 : 60);
                _controlsRt.anchoredPosition = new Vector2(-12, 12);
                _watchBg.enabled = true;
                _controlsRt.gameObject.SetActive(true);
                _spectatorButtons.SetActive(true);
                ((RectTransform)_spectatorButtons.transform).anchoredPosition = new Vector2(0, _replayMode ? 0 : -26);
                _speedGroup.anchoredPosition = new Vector2(0, _replayMode ? 0 : -26);
            }
            else _controlsRt.gameObject.SetActive(false); // your own duel: speed is in the gear

            // The detail column: beside the side panel in the developer layout; in play, a column at either edge.
            if (!_playLayout)
            {
                _detailGroup.anchorMin = _side2.anchorMin;
                _detailGroup.anchorMax = _side2.anchorMax;
                _detailGroup.anchoredPosition = Vector2.zero;
                _detailFade.alpha = 1;
                _detailFade.blocksRaycasts = _detailFade.interactable = true;
            }
            else DockDetail();

            // Attract mode (a bot duel behind the title): nothing to click, so none of the controls show.
            if (_attract)
            {
                _controlsRt.gameObject.SetActive(false);
                _headerButtons.SetActive(false);
                _headerTitle.gameObject.SetActive(false);
                _header.gameObject.SetActive(false);
                _gearMenu.gameObject.SetActive(false);
                if (_historyOpen) ToggleHistory();
            }
            if (_headerRt.Find("Menu") is Transform menuButton) menuButton.gameObject.SetActive(!_attract);
            _handsRoot.SetActive(!_humanMode && !_attract);
        }

        private bool _attract;

        /// <summary>
        /// Attract mode: the duel plays behind the title screen, so the board hides its controls (the menu button, the
        /// title, History and the gear, the watch bar, the Hands chip). Set it off again when the title closes.
        /// </summary>
        public bool AttractMode
        {
            get => _attract;
            set { _attract = value; ApplyLayout(); }
        }

        private static readonly List<Graphic> PanelGraphics = new List<Graphic>();

        /// <summary>
        /// The detail panel, while it's shown, is one solid thing over the mat: its backing takes the pointer everywhere in
        /// it (no click or hover reaches a card you can't see under it), and only its own controls inside answer. The plates
        /// within it don't take the pointer of their own. Hidden (slid out), the panel takes nothing (_detailFade), so cards
        /// in the corners under it are clicked as before.
        /// </summary>
        private void UnblockPanelBackgrounds()
        {
            if (_detailGroup == null) return;
            _detailGroup.GetComponentsInChildren(true, PanelGraphics);
            foreach (var g in PanelGraphics)
            {
                if (g == _infoBg || g == _hoverBg) { g.raycastTarget = true; continue; } // the backings: they block
                if (g.raycastTarget && g.GetComponent<UnityEngine.EventSystems.IEventSystemHandler>() == null) g.raycastTarget = false;
            }
            PanelGraphics.Clear();
        }

        private void DockDetail()
        {
            _detailGroup.anchorMin = new Vector2(_detailOnLeft ? 0 : 1 - ColumnWidth, 0.08f);
            _detailGroup.anchorMax = new Vector2(_detailOnLeft ? ColumnWidth : 1, 0.955f);
            _detailGroup.offsetMin = _detailGroup.offsetMax = Vector2.zero;
        }

        /// <summary>A card's detail was shown: in play, the panel slides in (on the side away from the pointer).</summary>
        private void DetailShown()
        {
            UnblockPanelBackgrounds();
            if (!_playLayout || _detailGroup == null) return;
            bool fromPointer = CardView.PointerCard != null;
            if (_detailSlide < 0.05f)
            {
                _detailOnLeft = _historyOpen || _stripHover || (fromPointer && PointerPosition().x > Screen.width * 0.62f);
                DockDetail();
            }
            // Shown by the duel (not a hover): it stays a few seconds.
            _detailShownUntil = Time.unscaledTime + (fromPointer ? 0f : 3.5f);
        }

        /// <summary>For checks: the top bar's title, and whether the floating watch controls are up.</summary>
        public string HeaderTitle => _headerTitle.text;
        public bool WatchBarShowing => _playLayout && _controlsRt.gameObject.activeInHierarchy && _watchBg.enabled;

        /// <summary>For checks: whether the card detail is showing (always in the developer layout).</summary>
        public bool DetailVisible => !_playLayout || _detailFade.alpha > 0.5f;

        private static Vector2 PointerPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return UnityEngine.InputSystem.Mouse.current?.position.ReadValue() ?? Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }

        /// <summary>For checks: the real mouse doesn't hold the detail open (wherever it was left on the screen).</summary>
        public static bool DebugIgnorePointer;

        /// <summary>Called every frame: the detail panel comes and goes, and the gear's labels stay current.</summary>
        private void UpdatePlayChrome()
        {
            if (_detailGroup == null) return;
            UpdateToast();
            UpdateCallouts();
            UpdateHistoryStrip();
            if (!_playLayout) return;
            float now = Time.unscaledTime;
            if (CardView.PointerCard != null && !DebugIgnorePointer) _pointerSeenAt = now;
            bool overPanel = _detailSlide > 0.5f && !DebugIgnorePointer && RectTransformUtility.RectangleContainsScreenPoint(_detailGroup, PointerPosition(), null);
            bool want = !_logOpen && _detail.Shown != null
                        && (_pinned != null || now - _pointerSeenAt < 0.45f || now < _detailShownUntil || overPanel);
            float target = want ? 1 : 0;
            _detailSlide = FxSettings.ReducedMotion ? target : Mathf.MoveTowards(_detailSlide, target, Time.unscaledDeltaTime * 7f);
            _detailFade.alpha = _detailSlide;
            _detailFade.blocksRaycasts = _detailFade.interactable = _detailSlide > 0.5f;
            FadeStrip(); // this frame's slide, so the strip is never over the panel even for a frame
            float eased = 1 - (1 - _detailSlide) * (1 - _detailSlide);
            // It slides out from the board toward its edge, so it is never cut off by the screen's edge.
            _detailGroup.anchoredPosition = new Vector2((1 - eased) * 60f * (_detailOnLeft ? 1 : -1), 0);
            if (_gearMenu.gameObject.activeSelf && _gearSpeed != null) _gearSpeed.text = _speedLabel.text;
        }

        // ---- The gear: speed, Chain, the text log, Save replay, Settings, the developer panel ----------------

        private Text? _gearSpeed;

        public void ToggleGear()
        {
            if (_gearMenu.gameObject.activeSelf) { _gearMenu.gameObject.SetActive(false); return; }
            if (_historyOpen) ToggleHistory();
            for (int i = _gearMenu.childCount - 1; i >= 0; i--) Destroy(_gearMenu.GetChild(i).gameObject);
            var rows = new List<(string label, Action<RectTransform> build)>
            {
                ("Speed", row =>
                {
                    Ui.Button(row, "-", new Vector2(-60, 0), new Vector2(44, 34), () => SpeedChanged?.Invoke(-1));
                    _gearSpeed = Ui.Text(Ui.Placed("Value", row, new Vector2(0, 0), new Vector2(70, 34)), _speedLabel.text, 16, Color.white);
                    Ui.Button(row, "+", new Vector2(60, 0), new Vector2(44, 34), () => SpeedChanged?.Invoke(+1));
                }),
            };
            if (_humanMode)
                rows.Add(("Chain prompts", row => GearButton(row, _responses.ToString(), () => { ResponsesClicked?.Invoke(); ToggleGear(); ToggleGear(); })));
            rows.Add(("Text log", row => GearButton(row, _logOpen ? "Close" : "Open", () => { ToggleGear(); ToggleLog(); })));
            if (_humanMode && !_replayMode)
                rows.Add(("This duel", row => GearButton(row, "Save replay", () => { ToggleGear(); SaveReplayClicked?.Invoke(); })));
            rows.Add(("Settings", row => GearButton(row, "Open", () => { ToggleGear(); SettingsClicked?.Invoke(); })));
            // The developer panel: developer mode only (BoardView.DevTools).
            if (DeveloperMode)
                rows.Add(("Free play tools (F12)", row => GearButton(row, "Show", () => { ToggleGear(); DevPanelClicked?.Invoke(); })));

            const float rowH = 46, width = 420;
            _gearMenu.sizeDelta = new Vector2(width, rows.Count * rowH + 16);
            var bg = Ui.Image(Ui.Stretched("Bg", _gearMenu, Vector2.zero, Vector2.one), new Color(0.06f, 0.08f, 0.13f, 0.97f));
            bg.gameObject.AddComponent<Outline>().effectColor = new Color(1, 1, 1, 0.15f);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = Ui.Rect(rows[i].label, _gearMenu);
                row.anchorMin = row.anchorMax = new Vector2(0, 1);
                row.pivot = new Vector2(0, 0.5f);
                row.anchoredPosition = new Vector2(0, -8 - rowH / 2 - i * rowH);
                row.sizeDelta = new Vector2(width, rowH);
                Ui.Text(Ui.Placed("Label", row, new Vector2(-95, 0), new Vector2(210, 34)), rows[i].label, 16, new Color(0.85f, 0.88f, 0.95f), TextAnchor.MiddleLeft);
                var right = Ui.Rect("Right", row);
                right.anchorMin = right.anchorMax = new Vector2(1, 0.5f);
                right.pivot = new Vector2(1, 0.5f);
                right.anchoredPosition = new Vector2(-12, 0);
                right.sizeDelta = new Vector2(190, rowH);
                rows[i].build(right);
            }
            _gearMenu.gameObject.SetActive(true);
            _gearMenu.SetAsLastSibling();
        }

        private static void GearButton(RectTransform row, string label, Action click) =>
            Ui.Button(row, label, Vector2.zero, new Vector2(180, 34), click);

        /// <summary>For checks: the gear menu's row labels (empty when closed).</summary>
        public List<string> GearRows()
        {
            var list = new List<string>();
            if (!_gearMenu.gameObject.activeSelf) return list;
            for (int i = 0; i < _gearMenu.childCount; i++) if (_gearMenu.GetChild(i).name != "Bg") list.Add(_gearMenu.GetChild(i).name);
            return list;
        }

        // ---- History: the recent-actions strip and the drawer --------------------------------------------------

        private DuelHistory? _history;
        private int _historyShown = -1;
        private bool _historyOpen;
        /// <summary>A recent-actions tile is being hovered: its card's detail opens on the left, clear of the strip.</summary>
        private bool _stripHover;
        private CanvasGroup _stripFade = null!;
        private RectTransform _strip = null!, _drawer = null!, _drawerRows = null!, _stripTip = null!;
        private Text _stripTipText = null!;
        private ScrollRect _drawerScroll = null!;

        /// <summary>The duel's history, shown as the strip and in the drawer (null clears them).</summary>
        public void SetHistory(DuelHistory? history)
        {
            _history = history;
            _historyShown = -1;
            if (_historyOpen) ToggleHistory();
        }

        /// <summary>For checks: the strip's tiles, and whether the drawer is open with how many rows.</summary>
        public int StripTiles => _strip == null ? 0 : _strip.childCount - 1;
        public (bool open, int rows) DrawerState => (_historyOpen, _drawerRows == null ? 0 : _drawerRows.childCount);

        private static string IconFor(HistoryKind kind) => kind switch
        {
            HistoryKind.Summon => "summon",
            HistoryKind.Activate => "activate",
            HistoryKind.Set => "set",
            HistoryKind.Attack => "attack",
            HistoryKind.Destroyed or HistoryKind.Banished => "removal",
            HistoryKind.Negated => "negate",
            HistoryKind.Win => "crown",
            _ => "",
        };

        private Color OwnerColor(int player) => player == (HumanSeat >= 0 ? HumanSeat : 0) ? Ui.Hex("#4FA3FF") : Ui.Hex("#FF5A5A");

        private void BuildHistory()
        {
            _strip = Ui.Rect("Recent", _canvas);
            _strip.anchorMin = _strip.anchorMax = new Vector2(1, 0.5f);
            _strip.pivot = new Vector2(1, 0.5f);
            _strip.anchoredPosition = new Vector2(-10, 10);
            _strip.sizeDelta = new Vector2(66, 6 * 80 + 24);
            _strip.SetSiblingIndex(_detailGroup.GetSiblingIndex());
            _stripFade = _strip.gameObject.AddComponent<CanvasGroup>();
            Ui.Text(Ui.Placed("Head", _strip, new Vector2(0, (6 * 80 + 24) / 2f - 10), new Vector2(70, 18)), "RECENT", 13, new Color(1, 1, 1, 0.7f));

            _stripTip = Ui.Rect("Tip", _canvas);
            _stripTip.anchorMin = _stripTip.anchorMax = new Vector2(1, 0.5f);
            _stripTip.pivot = new Vector2(1, 0.5f);
            _stripTip.sizeDelta = new Vector2(360, 54);
            Ui.Image(_stripTip, new Color(0.04f, 0.06f, 0.1f, 0.95f)).raycastTarget = false;
            _stripTipText = Ui.Text(Ui.Stretched("Text", _stripTip, Vector2.zero, Vector2.one), "", 15, Color.white);
            _stripTipText.rectTransform.offsetMin = new Vector2(12, 4);
            _stripTipText.rectTransform.offsetMax = new Vector2(-12, -4);
            _stripTip.gameObject.SetActive(false);

            // The drawer: the whole duel by turn, on the right under the top bar.
            _drawer = Ui.Rect("History", _canvas);
            _drawer.anchorMin = new Vector2(1, 0);
            _drawer.anchorMax = new Vector2(1, 0.955f);
            _drawer.pivot = new Vector2(1, 0.5f);
            _drawer.sizeDelta = new Vector2(560, 0);
            _drawer.anchoredPosition = Vector2.zero;
            Ui.Image(_drawer, new Color(0.05f, 0.07f, 0.11f, 0.97f));
            var head = Ui.Rect("Head", _drawer);
            head.anchorMin = new Vector2(0, 1);
            head.anchorMax = new Vector2(1, 1);
            head.pivot = new Vector2(0.5f, 1);
            head.sizeDelta = new Vector2(0, 56);
            Ui.Text(Ui.Placed("Title", head, new Vector2(-120, 0), new Vector2(280, 40)), "DUEL HISTORY", 20, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            Ui.Button(head, "Close", new Vector2(220, 0), new Vector2(90, 36), ToggleHistory);
            var viewport = Ui.Rect("Viewport", _drawer);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(10, 10);
            viewport.offsetMax = new Vector2(-10, -60);
            Ui.Image(viewport, new Color(0, 0, 0, 0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            _drawerRows = Ui.Rect("Rows", viewport);
            _drawerRows.anchorMin = new Vector2(0, 1);
            _drawerRows.anchorMax = new Vector2(1, 1);
            _drawerRows.pivot = new Vector2(0.5f, 1);
            _drawerScroll = viewport.gameObject.AddComponent<ScrollRect>();
            _drawerScroll.viewport = viewport;
            _drawerScroll.content = _drawerRows;
            _drawerScroll.horizontal = false;
            _drawerScroll.movementType = ScrollRect.MovementType.Clamped;
            _drawerScroll.scrollSensitivity = 40;
            _drawer.gameObject.SetActive(false);
        }

        /// <summary>Redraws the strip when the history has grown (called every frame).</summary>
        private void UpdateHistoryStrip()
        {
            // Not while the decision list is up at the right (the strip would sit on it).
            bool show = _playLayout && _history != null && !_historyOpen && !_logOpen && !_promptRoot.gameObject.activeSelf
                        && _history.Entries.Any(e => e.Kind != HistoryKind.Turn);
            _strip.gameObject.SetActive(show);
            if (!show) { _stripTip.gameObject.SetActive(false); return; }
            FadeStrip();
            if (_history!.Version == _historyShown) return;
            _historyShown = _history.Version;
            for (int i = _strip.childCount - 1; i >= 1; i--) Destroy(_strip.GetChild(i).gameObject);
            var recent = _history.Entries.Where(e => e.Kind != HistoryKind.Turn).Reverse().Take(6).ToList();
            float top = (6 * 80 + 24) / 2f - 24;
            for (int i = 0; i < recent.Count; i++) Tile(recent[i], new Vector2(0, top - 38 - i * 80));
            if (_historyOpen) DrawHistoryDrawer();
        }

        /// <summary>
        /// The strip gives way to the card detail sliding in at the right (it would sit on the text): gone by a quarter of
        /// the slide, before the panel shows, and always drawn under the panel. Called again once the slide has moved.
        /// </summary>
        private void FadeStrip()
        {
            if (!_strip.gameObject.activeSelf) return;
            float fade = _detailOnLeft ? 1 : Mathf.Clamp01(1 - _detailSlide * 4);
            if (_strip.GetSiblingIndex() > _detailGroup.GetSiblingIndex()) _strip.SetSiblingIndex(_detailGroup.GetSiblingIndex());
            _stripFade.alpha = fade;
            _stripFade.blocksRaycasts = fade > 0.5f;
            if (fade < 0.5f) _stripTip.gameObject.SetActive(false);
        }

        private void Tile(HistoryEntry e, Vector2 at)
        {
            var tile = Ui.Placed("Tile", _strip, at, new Vector2(56, 76));
            Ui.Image(tile, OwnerColor(e.Player));
            var face = Ui.Stretched("Face", tile, Vector2.zero, Vector2.one);
            face.offsetMin = new Vector2(3, 3);
            face.offsetMax = new Vector2(-3, -3);
            var art = e.Code != 0 ? CardView.Art?.Get(e.Code) : null;
            if (art != null) { var raw = face.gameObject.AddComponent<RawImage>(); raw.texture = art; raw.raycastTarget = false; }
            else
            {
                Ui.Image(face, new Color(0.1f, 0.12f, 0.18f, 1)).raycastTarget = false;
                var label = e.Kind == HistoryKind.Damage ? $"−{e.Amount}" : e.Kind == HistoryKind.Recover ? $"+{e.Amount}" : e.Kind == HistoryKind.Set ? "SET" : "";
                if (label.Length > 0) Ui.Text(face, label, 14, e.Kind == HistoryKind.Damage ? Ui.Hex("#FF8A8A") : Color.white).fontStyle = FontStyle.Bold;
            }
            // Whose it is in words, on a band at the bottom; what happened as an icon in the corner.
            var band = Ui.Placed("Who", tile, new Vector2(0, -38 + 9.5f), new Vector2(56, 19));
            Ui.Image(band, new Color(0, 0, 0, 0.72f)).raycastTarget = false;
            var whose = Ui.Text(band, OwnerTag(e.Player), 13, Color.white);
            whose.fontStyle = FontStyle.Bold;
            whose.resizeTextForBestFit = true; // a six-letter name still fits
            whose.resizeTextMinSize = 10;
            whose.resizeTextMaxSize = 13;
            var icon = IconFor(e.Kind);
            if (icon.Length > 0)
            {
                var badge = Ui.Placed("Kind", tile, new Vector2(-22, 32), new Vector2(24, 24));
                var bg = badge.gameObject.AddComponent<RawImage>();
                bg.texture = Icons.Round;
                bg.color = new Color(0.05f, 0.06f, 0.1f, 0.95f);
                bg.raycastTarget = false;
                var glyph = Ui.Placed("Icon", badge, Vector2.zero, new Vector2(18, 18)).gameObject.AddComponent<RawImage>();
                glyph.texture = Icons.For(icon);
                glyph.color = e.Kind is HistoryKind.Destroyed or HistoryKind.Banished or HistoryKind.Negated ? Ui.Hex("#FF8A8A") : Ui.Hex("#FFC940");
                glyph.raycastTarget = false;
            }
            var hover = tile.gameObject.AddComponent<HoverTip>();
            hover.Entered = () =>
            {
                if (e.Code != 0 && _cards.Get(e.Code) is { } info)
                {
                    _stripHover = true;
                    ShowInfo(info);
                    _stripHover = false;
                }
                _stripTipText.text = e.Text;
                _stripTip.anchoredPosition = new Vector2(-84, _strip.anchoredPosition.y + tile.anchoredPosition.y);
                _stripTip.gameObject.SetActive(true);
                _stripTip.SetAsLastSibling();
            };
            hover.Exited = () => _stripTip.gameObject.SetActive(false);
            hover.Clicked = ToggleHistory;
        }

        public void ToggleHistory()
        {
            _historyOpen = !_historyOpen;
            if (_historyOpen) _gearMenu.gameObject.SetActive(false);
            _stripTip.gameObject.SetActive(false);
            DrawHistoryDrawer();
        }

        private void DrawHistoryDrawer()
        {
            _drawer.gameObject.SetActive(_historyOpen);
            if (!_historyOpen) return;
            _drawer.SetAsLastSibling();
            for (int i = _drawerRows.childCount - 1; i >= 0; i--) Destroy(_drawerRows.GetChild(i).gameObject);
            float y = 0;
            foreach (var e in _history?.Entries ?? (IReadOnlyList<HistoryEntry>)Array.Empty<HistoryEntry>())
            {
                if (e.Kind == HistoryKind.Turn)
                {
                    var turn = Ui.Box(_drawerRows, 0, y + 8, 540, 30);
                    Ui.Image(turn, new Color(1, 1, 1, 0.06f)).raycastTarget = false;
                    Ui.Text(Ui.Box(turn, 12, 0, 500, 30), e.Text, 15, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                    y += 44;
                    continue;
                }
                var row = Ui.Box(_drawerRows, 0, y, 540, 52);
                var stripe = Ui.Box(row, 0, 4, 4, 44);
                Ui.Image(stripe, OwnerColor(e.Player)).raycastTarget = false;
                var thumb = Ui.Box(row, 12, 2, 34, 48);
                var art = e.Code != 0 ? CardView.Art?.Get(e.Code) : null;
                if (art != null) { var raw = thumb.gameObject.AddComponent<RawImage>(); raw.texture = art; raw.raycastTarget = false; }
                else Ui.Image(thumb, new Color(0.12f, 0.14f, 0.2f, 1)).raycastTarget = false;
                // Whose it is in a column of its own (every row's text starts at the same place); "YOU  You activate..."
                // drops the repeated word: "YOU  activate...".
                var owner = OwnerTag(e.Player);
                var said = e.Text;
                if (owner == "YOU" && said.StartsWith("You ", StringComparison.Ordinal)) said = said.Substring(4);
                var tag = Ui.Text(Ui.Box(row, 58, 0, 52, 52), owner, 13, Ui.Hex("#9AA3B5"), TextAnchor.MiddleLeft);
                tag.fontStyle = FontStyle.Bold;
                tag.resizeTextForBestFit = true; // a six-letter name (when watching) still fits the column
                tag.resizeTextMinSize = 10;
                tag.resizeTextMaxSize = 13;
                var text = Ui.Text(Ui.Box(row, 112, 0, 372, 52), said, 15, Color.white, TextAnchor.MiddleLeft);
                text.supportRichText = true;
                var icon = IconFor(e.Kind);
                if (icon.Length > 0)
                {
                    var glyph = Ui.Box(row, 496, 13, 26, 26).gameObject.AddComponent<RawImage>();
                    glyph.texture = Icons.For(icon);
                    glyph.color = e.Kind is HistoryKind.Destroyed or HistoryKind.Banished or HistoryKind.Negated ? Ui.Hex("#FF8A8A") : Ui.Hex("#FFC940");
                    glyph.raycastTarget = false;
                }
                if (e.Code != 0 && _cards.Get(e.Code) is { } info)
                {
                    Ui.Image(row, new Color(0, 0, 0, 0.001f));
                    row.gameObject.AddComponent<HoverTip>().Entered = () => ShowInfo(info);
                }
                y += 56;
            }
            _drawerRows.sizeDelta = new Vector2(0, y + 10);
            Canvas.ForceUpdateCanvases();
            _drawerScroll.verticalNormalizedPosition = 0; // the newest at the bottom, in view
        }

        // ---- After the duel: what's next, on the result banner -------------------------------------------------

        private RectTransform? _resultButtons;

        /// <summary>Buttons under the result banner (Rematch, New matchup, Menu); null clears them.</summary>
        public void SetResultButtons(IReadOnlyList<(string label, Action click)>? buttons)
        {
            if (_resultButtons != null) Destroy(_resultButtons.gameObject);
            _resultButtons = null;
            if (buttons == null || buttons.Count == 0) return;
            // On the banner's dark backing, carried on down under the buttons (they never float over the cards).
            var row = Ui.Placed("Actions", _bannerRoot, new Vector2(0, -_bannerRoot.sizeDelta.y / 2 - 40), new Vector2(_bannerRoot.sizeDelta.x, 80));
            Ui.Image(row, new Color(0, 0, 0, 0.8f));
            for (int i = 0; i < buttons.Count; i++)
            {
                var (label, click) = buttons[i];
                Ui.Button(row, label, new Vector2((i - (buttons.Count - 1) / 2f) * 230, 0), new Vector2(210, 50), click);
            }
            _resultButtons = row;
        }

        /// <summary>For checks: the result banner's button labels.</summary>
        public List<string> ResultButtons => _resultButtons == null ? new List<string>()
            : _resultButtons.GetComponentsInChildren<Text>().Select(t => t.text).ToList();

        // ---- Messages for what you'd miss ----------------------------------------------------------------------

        private RectTransform? _toast;
        private float _toastUntil;

        /// <summary>For checks: the message showing (empty when none).</summary>
        public string ToastText { get; private set; } = "";

        /// <summary>
        /// A short message at the top of the board for plays the board doesn't make obvious: the opponent's
        /// activations (and what they answer or target), your cards negated, destroyed or banished. Skipped at the
        /// fastest speeds; one at a time, the newest replacing the last.
        /// </summary>
        public void Toast(HistoryKind kind, uint code, string text)
        {
            if (Fx.Tempo <= 0) return;
            // The opponent's activations have their callout at the top right: what they answer or target goes there.
            if (kind == HistoryKind.Activate) { CalloutContext(code, HumanSeat >= 0 ? 1 - HumanSeat : 1, text); return; }
            if (_toast != null) Destroy(_toast.gameObject);
            var toast = Ui.Rect("Toast", _canvas);
            _toast = toast;
            toast.anchorMin = toast.anchorMax = new Vector2(0.5f, 1);
            toast.pivot = new Vector2(0.5f, 1);
            // Below the top bar, or below a banner at the top of the board (see ToastTop).
            toast.sizeDelta = new Vector2(Mathf.Clamp(text.Length * 9.5f + 90, 320, 880), 48);
            toast.anchoredPosition = new Vector2(_playLayout ? -120 : -260, -62);
            toast.anchoredPosition = new Vector2(toast.anchoredPosition.x, -ToastTop(toast));
            Ui.Image(toast, new Color(0.05f, 0.06f, 0.1f, 0.94f)).raycastTarget = false;
            bool bad = kind != HistoryKind.Activate;
            var edge = Ui.Placed("Edge", toast, new Vector2(-toast.sizeDelta.x / 2 + 3, 0), new Vector2(6, 48));
            Ui.Image(edge, bad ? Ui.Hex("#FF7070") : Ui.Hex("#FFC940")).raycastTarget = false;
            var icon = Ui.Placed("Icon", toast, new Vector2(-toast.sizeDelta.x / 2 + 32, 0), new Vector2(26, 26)).gameObject.AddComponent<RawImage>();
            icon.texture = Icons.For(IconFor(kind));
            icon.color = bad ? Ui.Hex("#FF8A8A") : Ui.Hex("#FFC940");
            icon.raycastTarget = false;
            var label = Ui.Text(Ui.Stretched("Text", toast, Vector2.zero, Vector2.one), text, 18, Color.white, TextAnchor.MiddleLeft);
            label.rectTransform.offsetMin = new Vector2(56, 0);
            label.rectTransform.offsetMax = new Vector2(-14, 0);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 18;
            toast.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            ToastText = text;
            _toastUntil = Time.unscaledTime + 3.2f;
        }

        private void UpdateToast()
        {
            if (_toast == null) return;
            // A banner can come or go while it shows: it keeps below whatever is at the top.
            _toast.anchoredPosition = new Vector2(_toast.anchoredPosition.x, -ToastTop(_toast));
            float left = _toastUntil - Time.unscaledTime;
            var group = _toast.GetComponent<CanvasGroup>();
            group.alpha = Mathf.Clamp01(left / 0.4f) * Mathf.Clamp01((3.2f - left) / 0.15f + (FxSettings.ReducedMotion ? 1 : 0));
            if (left > 0) return;
            Destroy(_toast.gameObject);
            _toast = null;
            ToastText = "";
        }

        // ---- Banners at the top of the board: messages and callouts keep below them ----------------------------

        /// <summary>
        /// What sits at the top of the board right now: a decision's banner (a field pick, a position) or its "Show" pill,
        /// the Check Field pill, and a lesson's banner.
        /// </summary>
        private IEnumerable<RectTransform> TopBanners()
        {
            if (Dialogs?.Banner is RectTransform banner) yield return banner;
            if (_checkPill != null && _checkPill.gameObject.activeInHierarchy) yield return _checkPill;
            if (_lessonBanner != null && _lessonBanner.gameObject.activeInHierarchy) yield return _lessonBanner;
            if (_comboMeter != null && _comboMeter.gameObject.activeInHierarchy) yield return _comboMeter;
        }

        /// <summary>A rect's left, right and bottom edges in the field's coordinates (from its center).</summary>
        private (float left, float right, float bottom) InField(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 min = _field.InverseTransformPoint(corners[0]), max = _field.InverseTransformPoint(corners[2]);
            return (Mathf.Min(min.x, max.x), Mathf.Max(min.x, max.x), Mathf.Min(min.y, max.y));
        }

        /// <summary>How far below the field's top edge a rect ends.</summary>
        private float BelowFieldTop(RectTransform rt) => _field.rect.yMax - InField(rt).bottom;

        /// <summary>The toast's distance from the top of the screen: under the top bar and 8 px under any banner it meets.</summary>
        private float ToastTop(RectTransform toast)
        {
            float fieldTop = ((RectTransform)_canvas).rect.height * (1 - _field.anchorMax.y);
            float top = 62;
            // Its own sides, from its x and width (its height may be stale while it moves).
            var (left, right, _) = InField(toast);
            foreach (var banner in TopBanners())
            {
                var b = InField(banner);
                if (b.right > left && b.left < right) top = Mathf.Max(top, fieldTop + BelowFieldTop(banner) + 8);
            }
            return top;
        }

        /// <summary>
        /// Where the callout stack starts, down from the field's top: 12 px, or 8 px under a banner or the toast where they
        /// share the column (the callouts then never clip under them; the chain panel follows the callouts).
        /// </summary>
        private float CalloutTop()
        {
            float top = 12;
            float right = _field.rect.xMax - ColumnMargin + _columnDodge, left = right - CalloutWidth;
            // A field pick's Confirm / Cancel discs (and their labels) sit near the top right: the stack goes below them.
            var obstacles = TopBanners().Concat(Dialogs != null ? Dialogs.Discs : Enumerable.Empty<RectTransform>());
            foreach (var banner in obstacles)
            {
                var b = InField(banner);
                if (b.right > left && b.left < right) top = Mathf.Max(top, BelowFieldTop(banner) + 8);
            }
            if (_toast != null)
            {
                var t = InField(_toast);
                if (t.right > left && t.left < right) top = Mathf.Max(top, BelowFieldTop(_toast) + 8);
            }
            return top;
        }

        /// <summary>Hover and click for things that aren't cards or buttons (history tiles and rows).</summary>
        private sealed class HoverTip : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler,
            UnityEngine.EventSystems.IPointerClickHandler
        {
            public Action? Entered, Exited, Clicked;
            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => Entered?.Invoke();
            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => Exited?.Invoke();
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e) => Clicked?.Invoke();
        }
    }
}
