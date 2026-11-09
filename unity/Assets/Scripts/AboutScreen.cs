using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// About (from the main menu): the game's name, version and copyright, the plain-language license notice and
    /// disclaimer, credits, and buttons to read the license and the third-party notices in the game, open the licenses
    /// folder, and open the source code page (AGPL section 5(d), "Appropriate Legal Notices"). The two texts open in a
    /// scrolling reader over the screen. Mouse, arrows + Enter + Esc, or a controller (d-pad, A, B).
    /// </summary>
    public sealed class AboutScreen : MonoBehaviour
    {
        public const string Copyright = "Copyright (C) 2026 Jake Joseph and the Duel Circuit authors.";

        public const string LicenseNotice =
            "Duel Circuit is free software: you can play, copy, share and change it under the GNU Affero General Public "
            + "License, version 3 or (at your option) any later version, with an extra permission to build it with Unity. "
            + "If you share a changed version, share its source code too.\n\n"
            + "It comes with NO WARRANTY, to the extent permitted by law.\n\n"
            + "The art, story and characters are under Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0). "
            + "The name \"Duel Circuit\" and the logo aren't covered by these licenses. Parts made by others keep their own "
            + "licenses: see Third-party notices.";

        public const string Disclaimer =
            "Duel Circuit is an unofficial fan game. It is not made, approved or supported by Konami or Shueisha. "
            + "Yu-Gi-Oh!, card names and card text belong to their owners. No card pictures ship with the game. "
            + "It's free, so please don't sell it.";

        public const string Credits =
            "• Rules engine: ygopro-core, by edo9300 and contributors.\n"
            + "• Card scripts, card database and ban lists: Project Ignis (EDOPro).\n"
            + "• Card-set data: YGOPRODeck.\n"
            + "• Historic decks and ban lists: Format Library and yugiohmeta.com.\n"
            + "• Made with Unity.";

        /// <summary>The buttons along the bottom, left to right.</summary>
        public static readonly string[] ButtonLabels = { "Back", "View license", "Third-party notices", "Open licenses folder", "Source code" };

        private RectTransform _root = null!, _main = null!, _reader = null!, _readerContent = null!, _readerViewport = null!;
        private ScrollRect _readerScroll = null!;
        private Button _readerBack = null!;
        private Text _readerTitle = null!, _readerFrom = null!, _status = null!;
        private readonly List<(string label, Button button)> _buttons = new List<(string, Button)>();
        private readonly List<(string what, RectTransform rt)> _layout = new List<(string, RectTransform)>();
        private readonly ArrowNav _nav = new ArrowNav();
        private RectTransform? _frame;
        private int _focus, _openedFrame = -10;

        /// <summary>Back pressed on the screen itself (not the reader): the menu takes over.</summary>
        public event Action? Closed;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool ReaderOpen => IsOpen && _reader != null && _reader.gameObject.activeSelf;

        public void Init()
        {
            _root = Ui.Overlay(transform, "About", 36);
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            Build();
            _openedFrame = Time.frameCount;
            _nav.Used = false;
            Focus(1); // View license
        }

        public void Close() => _root.gameObject.SetActive(false);

        /// <summary>Back (the button, Esc, the controller's B): the reader closes first, then the screen.</summary>
        public void Back()
        {
            if (ReaderOpen) { CloseReader(); return; }
            Close();
            Closed?.Invoke();
        }

        private void Build()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            _buttons.Clear();
            _layout.Clear();
            _frame = null;
            var T = UiTheme.Current;
            Ui.Image(Ui.Stretched("Background", _root, Vector2.zero, Vector2.one), T.Deep);
            _main = Ui.Stretched("Main", _root, Vector2.zero, Vector2.one);

            var title = Ui.Box(_main, 110, 60, 1000, 100);
            Ui.Text(title, "ABOUT", 60, T.Sign, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _layout.Add(("title", title));
            Block("name", 110, 180, 900, 50, $"Duel Circuit   {GameInfo.Version}", 34, T.Text, bold: true);
            Block("copyright", 110, 232, 900, 30, Copyright, 20, T.TextBody);
            // Two columns: what you may do and the disclaimer on the left; credits and the source code on the right.
            Heading("license heading", 110, 290, 900, "FREE SOFTWARE, NO WARRANTY");
            Block("license", 110, 326, 900, 262, LicenseNotice, 20, T.TextSoft);
            Heading("disclaimer heading", 110, 610, 900, "DISCLAIMER");
            Block("disclaimer", 110, 646, 900, 110, Disclaimer, 20, T.TextSoft);

            Heading("credits heading", 1080, 290, 730, "CREDITS");
            Block("credits", 1080, 326, 730, 200, Credits, 20, T.TextSoft);
            Heading("source heading", 1080, 610, 730, "SOURCE CODE");
            var built = LicenseFiles.BuiltFrom();
            Block("source", 1080, 646, 730, 150,
                $"{LicenseFiles.SourceUrl.Replace("https://", "")}\nThe source code of this game, its rules engine included, is yours to get there."
                + (built != null ? $"\n{built}." : ""), 20, T.TextSoft);

            _status = Block("status", 110, 880, 1700, 40, "", 17, T.TextMuted);
            float x = 110;
            var widths = new[] { 200f, 280f, 320f, 340f, 260f };
            var actions = new Action[] { Back, ShowLicense, ShowNotices, OpenFolder, OpenSource };
            for (int i = 0; i < ButtonLabels.Length; i++)
            {
                var button = Ui.BoxButton(_main, x, 960, widths[i], 60, ButtonLabels[i], actions[i], 22);
                if (i == 1) button.targetGraphic.color = T.Primary; // the one red action: read the license
                _buttons.Add((ButtonLabels[i], button));
                _layout.Add(($"{ButtonLabels[i]} button", (RectTransform)button.transform));
                x += widths[i] + 20;
            }
            BuildReader(T);
        }

        private Text Block(string what, float x, float y, float w, float h, string text, int size, Color color, bool bold = false)
        {
            var rt = Ui.Box(_main, x, y, w, h);
            var t = Ui.Text(rt, text, size, color, TextAnchor.UpperLeft);
            t.supportRichText = false;
            if (bold) t.fontStyle = FontStyle.Bold;
            _layout.Add((what, rt));
            return t;
        }

        private void Heading(string what, float x, float y, float w, string text)
        {
            var rt = Ui.Box(_main, x, y, w, 30);
            Ui.Text(rt, text, 19, UiTheme.Current.Gold, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _layout.Add((what, rt));
        }

        /// <summary>The reader: a title, where the text came from, the text scrolling in a panel, Back and a hint.</summary>
        private void BuildReader(UiTheme T)
        {
            _reader = Ui.Stretched("Reader", _root, Vector2.zero, Vector2.one);
            Ui.Image(_reader, T.Deep); // covers the screen and its clicks
            // A reading column 1200 wide in the middle (the texts' lines stay a comfortable length).
            const float x = 360, w = 1200;
            _readerTitle = Ui.Text(Ui.Box(_reader, x, 40, w, 70), "", 44, T.Sign, TextAnchor.MiddleLeft);
            _readerTitle.fontStyle = FontStyle.Bold;
            _readerFrom = Ui.Text(Ui.Box(_reader, x, 112, w, 30), "", 15, T.TextMuted, TextAnchor.MiddleLeft);
            _readerFrom.supportRichText = false;
            _readerFrom.resizeTextForBestFit = true; // a long folder path shrinks to fit
            _readerFrom.resizeTextMinSize = 10;
            _readerFrom.resizeTextMaxSize = 15;

            _readerViewport = Ui.Box(_reader, x, 150, w, 780);
            Ui.Image(_readerViewport, T.Panel);
            _readerViewport.gameObject.AddComponent<RectMask2D>();
            _readerContent = Ui.Rect("Content", _readerViewport);
            _readerContent.anchorMin = new Vector2(0, 1);
            _readerContent.anchorMax = new Vector2(1, 1);
            _readerContent.pivot = new Vector2(0.5f, 1);
            _readerContent.sizeDelta = Vector2.zero;
            var column = _readerContent.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(40, 40, 30, 40);
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;
            _readerContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _readerScroll = _readerViewport.gameObject.AddComponent<ScrollRect>();
            _readerScroll.viewport = _readerViewport;
            _readerScroll.content = _readerContent;
            _readerScroll.horizontal = false;
            _readerScroll.movementType = ScrollRect.MovementType.Clamped;
            _readerScroll.scrollSensitivity = 40;

            // A scrollbar on the right: where you are in a long text, and a handle to drag.
            var bar = Ui.Box(_reader, x + w + 8, 150, 14, 780);
            Ui.Image(bar, T.Well);
            var area = Ui.Stretched("Area", bar, Vector2.zero, Vector2.one);
            var handle = Ui.Stretched("Handle", area, Vector2.zero, Vector2.one);
            var handleImage = Ui.Image(handle, T.Button);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            _readerScroll.verticalScrollbar = scrollbar;
            _readerScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            _readerBack = Ui.BoxButton(_reader, x, 960, 240, 60, "Back", CloseReader, 22);
            Ui.Text(Ui.Box(_reader, x + 270, 960, w - 270, 60),
                "Scroll with the mouse wheel, the arrow keys or the d-pad (Page Up / Page Down, LB / RB jump a page). Esc or B goes back.",
                16, T.TextMuted, TextAnchor.MiddleLeft);
            _reader.gameObject.SetActive(false);
        }

        /// <summary>View license: the AGPL's full text, then the Unity permission that goes with it.</summary>
        public void ShowLicense()
        {
            var agpl = LicenseFiles.Read(LicenseFiles.Doc.License, out bool found);
            var permission = LicenseFiles.Read(LicenseFiles.Doc.UnityPermission, out bool permissionFound);
            var text = agpl + "\n\n\n────────────────────────────────────────\n\n" + permission;
            // The AGPL is wrapped at 72 characters: its column sits in the middle of the panel.
            ShowReader("License", text, found || permissionFound, 220,
                LicenseFiles.Find(LicenseFiles.Doc.License), LicenseFiles.Find(LicenseFiles.Doc.UnityPermission));
        }

        /// <summary>Third-party notices: THIRD_PARTY_NOTICES.md, made plain.</summary>
        public void ShowNotices()
        {
            var text = LicenseFiles.Read(LicenseFiles.Doc.ThirdParty, out bool found);
            ShowReader("Third-party notices", text, found, 40, LicenseFiles.Find(LicenseFiles.Doc.ThirdParty));
        }

        private void ShowReader(string title, string text, bool found, int margin, params string?[] files)
        {
            var T = UiTheme.Current;
            _readerTitle.text = title.ToUpperInvariant();
            // Where it was read: the files' names, and their folder once.
            var shown = files.Where(f => f != null).Select(f => f!).ToList();
            bool oneFolder = shown.Select(Path.GetDirectoryName).Distinct().Count() == 1;
            _readerFrom.text = shown.Count == 0 ? $"Not in this copy of the game: read it at {LicenseFiles.SourceUrl}"
                : oneFolder ? $"From {string.Join(" and ", shown.Select(Path.GetFileName))} in {Path.GetDirectoryName(shown[0])}"
                : $"From {string.Join(" and ", shown)}";
            for (int i = _readerContent.childCount - 1; i >= 0; i--) DestroyImmediate(_readerContent.GetChild(i).gameObject);
            _readerContent.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(margin, margin, 30, 40);
            // A Text holds about 16,000 characters at most (65,000 vertices): long texts go in pieces, split at line ends.
            foreach (var piece in Pieces(text, 4000))
            {
                var t = Ui.Text(Ui.Rect("Piece", _readerContent), piece, 20, T.TextBody, TextAnchor.UpperLeft);
                t.supportRichText = false;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.lineSpacing = 1.05f;
            }
            _reader.gameObject.SetActive(true);
            _reader.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_readerContent);
            _readerScroll.verticalNormalizedPosition = 1;
            _readerContent.anchoredPosition = Vector2.zero;
            _status.text = found ? "" : $"{title}: the file isn't in this copy of the game. Read it at {LicenseFiles.SourceUrl}";
        }

        /// <summary>Text cut into pieces of at most <paramref name="max"/> characters, at line ends where it can be.</summary>
        public static List<string> Pieces(string text, int max)
        {
            var pieces = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int length = Math.Min(max, text.Length - start);
                if (start + length < text.Length)
                {
                    int cut = text.LastIndexOf('\n', start + length - 1, length);
                    if (cut > start) length = cut - start;
                }
                pieces.Add(text.Substring(start, length));
                start += length;
                if (start < text.Length && text[start] == '\n') start++; // the line end itself is the break between pieces
            }
            return pieces;
        }

        public void CloseReader()
        {
            if (_reader == null) return;
            _reader.gameObject.SetActive(false);
            Focus(_focus);
        }

        /// <summary>Open licenses folder: the folder with the files, in Finder or Explorer.</summary>
        public void OpenFolder()
        {
            var folder = LicenseFiles.Folder;
            if (!Directory.Exists(folder))
            {
                _status.text = $"This copy of the game has no licenses folder. They're all at {LicenseFiles.SourceUrl}";
                return;
            }
            var url = new Uri(Path.GetFullPath(folder).TrimEnd('/', '\\') + Path.DirectorySeparatorChar).AbsoluteUri;
            _status.text = $"Opened {folder}";
            OpenUrl(url);
        }

        /// <summary>Source code: the repository's page in the browser.</summary>
        public void OpenSource()
        {
            _status.text = $"Opened {LicenseFiles.SourceUrl} in your browser.";
            OpenUrl(LicenseFiles.SourceUrl);
        }

        /// <summary>For checks: what would have opened (a check run never opens a browser or a folder).</summary>
        public readonly List<string> DebugOpened = new List<string>();

        private void OpenUrl(string url)
        {
            if (DuelController.IsCheckRun)
            {
                DebugOpened.Add(url);
                _status.text += " (not opened: automated check)";
                return;
            }
            Application.OpenURL(url);
        }

        private void Focus(int index)
        {
            _focus = Mathf.Clamp(index, 0, _buttons.Count - 1);
            if (_buttons.Count == 0) return;
            ArrowNav.Mark(ref _frame, _main, (RectTransform)_buttons[_focus].button.transform, 6);
        }

        /// <summary>LB / RB on the reader: a page up or down.</summary>
        public void Page(int delta)
        {
            if (ReaderOpen) ScrollBy(delta * _readerViewport.rect.height * 0.9f);
        }

        private void ScrollBy(float pixels)
        {
            Canvas.ForceUpdateCanvases();
            float room = Mathf.Max(0, _readerContent.rect.height - _readerViewport.rect.height);
            var p = _readerContent.anchoredPosition;
            p.y = Mathf.Clamp(p.y + pixels, 0, room);
            _readerContent.anchoredPosition = p;
            _readerScroll.velocity = Vector2.zero;
        }

        private void Update()
        {
            if (!IsOpen || Time.frameCount == _openedFrame) return; // the press that opened it isn't also read here
            var probe = ReaderOpen ? _readerViewport : (RectTransform)_buttons[_focus].button.transform;
            var keys = _nav.Read(_root, probe);
            bool escape = false, pageUp = false, pageDown = false, home = false, end = false;
#if ENABLE_INPUT_SYSTEM
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (!(device is UnityEngine.InputSystem.Keyboard k)) continue;
                escape |= k.escapeKey.wasPressedThisFrame;
                pageUp |= k.pageUpKey.wasPressedThisFrame;
                pageDown |= k.pageDownKey.wasPressedThisFrame;
                home |= k.homeKey.wasPressedThisFrame;
                end |= k.endKey.wasPressedThisFrame;
            }
#else
            escape = Input.GetKeyDown(KeyCode.Escape);
            pageUp = Input.GetKeyDown(KeyCode.PageUp);
            pageDown = Input.GetKeyDown(KeyCode.PageDown);
            home = Input.GetKeyDown(KeyCode.Home);
            end = Input.GetKeyDown(KeyCode.End);
#endif
            if (escape && !ArrowNav.TypingSomewhere()) { Back(); return; }
            if (ReaderOpen)
            {
                if (keys.Move.y != 0) ScrollBy(keys.Move.y * 90f);
                if (pageUp) Page(-1);
                if (pageDown) Page(1);
                if (home) ScrollBy(-1e6f);
                if (end) ScrollBy(1e6f);
                if (keys.Enter) CloseReader(); // Enter on the reader: back to the buttons (its only button)
                return;
            }
            // One row of buttons: any arrow moves along it, stopping at the ends; Enter presses the highlighted one.
            int step = keys.Move.x != 0 ? keys.Move.x : keys.Move.y;
            if (step != 0) Focus(_focus + step);
            if (keys.Enter) _buttons[_focus].button.onClick.Invoke();
        }

        /// <summary>For checks: a button's rect by its label (the reader's Back with <paramref name="reader"/>).</summary>
        public RectTransform DebugButton(string label, bool reader = false) =>
            reader ? (RectTransform)_readerBack.transform : (RectTransform)_buttons.First(b => b.label == label).button.transform;

        /// <summary>For checks: the highlighted button's label.</summary>
        public string DebugFocusLabel => _buttons.Count > 0 ? _buttons[_focus].label : "";

        /// <summary>For checks: whether the keyboard's gold frame shows (highlight mode).</summary>
        public bool DebugFrameShown => _frame != null && _frame.gameObject.activeInHierarchy && _frame.GetComponent<KeyFrame>() is KeyFrame f && f.Shown;

        /// <summary>For checks: every text on the screen (the main page; the reader's too when it's open).</summary>
        public List<Text> DebugTexts(bool reader) =>
            (reader ? _reader : _main).GetComponentsInChildren<Text>().Where(t => t.isActiveAndEnabled).ToList();

        /// <summary>For checks: the reader's title, where it read from, and its whole text.</summary>
        public (string title, string from, string text) DebugReader =>
            (_readerTitle.text, _readerFrom.text, string.Join("\n", _readerContent.GetComponentsInChildren<Text>().Select(t => t.text)));

        /// <summary>For checks: how far the reader is scrolled (0 at the top) and how far it can go.</summary>
        public (float at, float room) DebugScroll
        {
            get
            {
                Canvas.ForceUpdateCanvases();
                return (_readerContent.anchoredPosition.y, Mathf.Max(0, _readerContent.rect.height - _readerViewport.rect.height));
            }
        }

        public string DebugStatus => _status.text;

        /// <summary>For checks: the main page's blocks and buttons (or the reader's parts), in the 1920x1080 layout from its top-left.</summary>
        public List<(string what, Rect rect)> DebugBoxes(bool reader)
        {
            var corners = new Vector3[4];
            Rect Of(RectTransform rt)
            {
                rt.GetWorldCorners(corners);
                var a = _root.InverseTransformPoint(corners[0]);
                var b = _root.InverseTransformPoint(corners[2]);
                var size = _root.rect.size;
                return Rect.MinMaxRect(a.x + size.x / 2, size.y / 2 - b.y, b.x + size.x / 2, size.y / 2 - a.y);
            }
            if (!reader) return _layout.Select(l => (l.what, Of(l.rt))).ToList();
            var boxes = new List<(string, Rect)>();
            foreach (Transform child in _reader)
            {
                if (!child.gameObject.activeSelf) continue;
                var name = child == _readerViewport ? "text panel" : child.GetComponentInChildren<Text>()?.text ?? child.name;
                boxes.Add((name.Length > 30 ? name.Substring(0, 30) : name, Of((RectTransform)child)));
            }
            return boxes;
        }
    }
}
