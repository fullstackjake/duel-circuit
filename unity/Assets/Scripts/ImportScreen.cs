using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Import Cards (from the main menu): the player's own card pictures, high-resolution pictures and card back, from a zip
    /// or image files, into mods/cards (docs/dev/CARD-IMPORT-SPEC-2026-11-12.md, Part D). One file dialog per section, a
    /// scan, one question when pictures already exist, a progress bar with Cancel, a result line. The scan and the import
    /// run on a background thread (<see cref="PictureImport"/>); this screen polls them in Update and touches Unity objects
    /// only on the main thread. Mouse, arrows + Enter + Esc, or a controller (d-pad, A, B).
    /// </summary>
    public sealed class ImportScreen : MonoBehaviour
    {
        public const string Privacy = "Your pictures stay on this computer. The game never shares or uploads them, and ships with none.";
        public const string Hint = "Arrows or d-pad to move, Enter or A to choose, Esc or B to go back.";

        public enum State { Idle, Scanning, Question, Problem, BackConfirm, Importing }

        private sealed class Section
        {
            public PictureKind Kind;
            public string Heading = "", Purpose = "", Group = "";
            public Text Count = null!, Path = null!;
            public Button Import = null!, Open = null!;
            public RectTransform? Preview;
        }

        /// <summary>Progress from the background thread: written there, read here once a frame.</summary>
        private sealed class Counter : IProgress<(int done, int total)>
        {
            private int _done, _total;
            public int Done => Volatile.Read(ref _done);
            public int Total => Volatile.Read(ref _total);
            public void Report((int done, int total) value)
            {
                Volatile.Write(ref _total, value.total);
                Volatile.Write(ref _done, value.done);
            }
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private RectTransform _root = null!, _main = null!, _activity = null!, _barFill = null!;
        private RectTransform? _modal, _frame;
        private Text _status = null!, _activityLine = null!, _activityNote = null!, _activityCount = null!;
        private RectTransform _barTrack = null!;
        private Button _activityCancel = null!, _back = null!;
        private readonly List<Section> _sections = new List<Section>();
        private readonly List<(string what, string group, RectTransform rt)> _layout = new List<(string, string, RectTransform)>();
        /// <summary>The buttons the keys move over now (the page's six and Back; Cancel; a modal's), and the highlighted one.</summary>
        private readonly List<Button> _keys = new List<Button>();
        private readonly HashSet<Button> _primary = new HashSet<Button>();
        private readonly ArrowNav _nav = new ArrowNav();
        private int _focus, _openedFrame = -10;

        private State _state;
        private int _section;
        private string[] _paths = Array.Empty<string>();
        private PictureImportPlan? _plan;
        private Task<PictureImportPlan>? _scan;
        private Task<PictureImportResult>? _run;
        private CancellationTokenSource? _cts;
        private Counter? _counter;
        private bool _stopping;
        private Texture2D? _newBack;
        /// <summary>For checks: the answer to give when the overwrite question or the card back's comes (null: wait for a press).</summary>
        private bool? _debugAnswer;

        /// <summary>Back pressed on the idle screen: the menu takes over.</summary>
        public event Action? Closed;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool Busy => _state != State.Idle;

        private static CardArt Art => CardView.Art!;

        public void Init()
        {
            _root = Ui.Overlay(transform, "Import", 36);
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            Build();
            _openedFrame = Time.frameCount;
            _nav.Used = false;
            if (_run != null)
            {
                // An import stopped by a close that hasn't ended yet: shown stopping, until it does.
                _activityLine.text = "Stopping...";
                ShowActivity(importing: true);
                _activityCancel.interactable = false;
                SetState(State.Importing, 0);
            }
            else SetState(State.Idle, 0);
        }

        /// <summary>Closes the screen; work under way is cancelled (a scan dropped, an import stopped between pictures).</summary>
        public void Close()
        {
            if (_state == State.Scanning) DropScan();
            _debugAnswer = null;
            _cts?.Cancel();
            _stopping = _run != null;
            CloseModal();
            if (_run == null) _state = State.Idle;
            _root.gameObject.SetActive(false);
        }

        /// <summary>Back (the button, Esc, the controller's B): cancels what's under way or answers a question with Cancel; else back to the menu.</summary>
        public void Back()
        {
            switch (_state)
            {
                case State.Idle:
                    Close();
                    Closed?.Invoke();
                    break;
                case State.Scanning: case State.Importing: CancelWork(); break;
                case State.Question: case State.BackConfirm: Cancelled(); break;
                case State.Problem: ProblemOk(); break;
            }
        }

        private void OnDestroy() => _cts?.Cancel();
        private void OnApplicationQuit() => _cts?.Cancel();

        // ---- Building the page ----------------------------------------------------------------------------------

        private void Build()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            _layout.Clear();
            _sections.Clear();
            _primary.Clear();
            _modal = null;
            _frame = null;
            var T = UiTheme.Current;
            Ui.Image(Ui.Stretched("Background", _root, Vector2.zero, Vector2.one), T.Deep);
            _main = Ui.Stretched("Main", _root, Vector2.zero, Vector2.one);

            var title = Ui.Box(_main, 110, 60, 1000, 100);
            Ui.Text(title, "IMPORT CARDS", 60, T.Sign, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
            _layout.Add(("title", "page", title));
            Label(_main, "page", "privacy", 110, 168, 1700, 30, Privacy, 18, T.TextSoft);

            AddSection(PictureKind.Standard, "images row", 220, "Card Images", "Pictures for the cards in duels, packs and the deck editor.");
            AddSection(PictureKind.Hd, "hd row", 388, "High Resolution Card Images", "Bigger pictures, used when a card is shown large. Optional.");
            AddSection(PictureKind.Back, "back row", 556, "Card Back", "The picture on the back of every card.");

            // The activity panel: Reading... / Importing... with Cancel (and a bar while importing). Hidden while idle.
            _activity = Ui.Box(_main, 110, 730, 1700, 130);
            Ui.Image(_activity, T.PanelDeep);
            _layout.Add(("activity", "page", _activity));
            _activityLine = Label(_activity, "activity", "line", 30, 16, 1380, 32, "", 22, T.Text, bold: true);
            _activityNote = Label(_activity, "activity", "note", 30, 56, 1380, 26, "A big zip can take a moment.", 18, T.TextSoft);
            _barTrack = Ui.Box(_activity, 30, 58, 1380, 22);
            Ui.Image(_barTrack, T.Well);
            _layout.Add(("bar track", "activity", _barTrack));
            _barFill = Ui.Stretched("Fill", _barTrack, Vector2.zero, new Vector2(0, 1));
            Ui.Image(_barFill, T.Meter).raycastTarget = false;
            _activityCount = Label(_activity, "activity", "count", 30, 88, 1380, 26, "", 18, T.TextSoft);
            _activityCancel = Ui.BoxButton(_activity, 1450, 37, 220, 56, "Cancel", CancelWork, 22);
            _layout.Add(("Cancel", "activity", (RectTransform)_activityCancel.transform));
            Hover(_activityCancel);
            _activity.gameObject.SetActive(false);

            _status = Label(_main, "page", "status", 110, 880, 1700, 40, "", 20, T.TextSoft);
            _status.resizeTextForBestFit = true; // a long result line (a failure's reason) shrinks to fit
            _status.resizeTextMinSize = 14;
            _status.resizeTextMaxSize = 20;
            _back = Ui.BoxButton(_main, 110, 960, 240, 60, "Back", Back, 22);
            _layout.Add(("Back button", "page", (RectTransform)_back.transform));
            Hover(_back);
            Label(_main, "page", "hint", 380, 960, 1430, 60, Hint, 16, T.TextMuted);
            RefreshRows();
        }

        private void AddSection(PictureKind kind, string group, float y, string heading, string purpose)
        {
            var T = UiTheme.Current;
            var row = Ui.Box(_main, 110, y, 1700, 148);
            Ui.Image(row, T.Panel);
            _layout.Add((group, "page", row));
            var s = new Section { Kind = kind, Heading = heading, Purpose = purpose, Group = group };
            Label(row, group, "heading", 30, 14, 1060, 32, heading, 24, T.Gold, bold: true);
            Label(row, group, "purpose", 30, 50, 1060, 24, purpose, 17, T.TextSoft);
            s.Count = Label(row, group, "count", 30, 78, 1060, 30, "", 22, T.Text, bold: true);
            s.Path = Label(row, group, "path", 30, 112, 1060, 22, "", 15, T.TextMuted);
            if (kind == PictureKind.Back)
            {
                // A 1 px well round the preview, so a dark back still shows its edge.
                Ui.Image(Ui.Box(row, 1119, 13, 85, 122), T.Well).raycastTarget = false;
                s.Preview = Ui.Box(row, 1120, 14, 83, 120);
                _layout.Add(("preview", group, s.Preview));
            }
            int index = _sections.Count;
            s.Import = Ui.BoxButton(row, 1240, 46, 200, 56, "Import...", () => Pick(index), 22);
            s.Open = Ui.BoxButton(row, 1460, 46, 200, 56, "Open folder", () => OpenFolder(index), 22);
            _layout.Add(("Import...", group, (RectTransform)s.Import.transform));
            _layout.Add(("Open folder", group, (RectTransform)s.Open.transform));
            Hover(s.Import);
            Hover(s.Open);
            _sections.Add(s);
        }

        private Text Label(RectTransform parent, string group, string what, float x, float y, float w, float h, string text, int size, Color color,
            bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rt = Ui.Box(parent, x, y, w, h);
            var t = Ui.Text(rt, text, size, color, anchor);
            t.supportRichText = false;
            if (bold) t.fontStyle = FontStyle.Bold;
            _layout.Add((what, group, rt));
            return t;
        }

        /// <summary>The mouse over a button moves the keys' highlight there (as on the menu).</summary>
        private void Hover(Button button)
        {
            Ui.Clickable((RectTransform)button.transform, null, hover: () =>
            {
                int at = _keys.IndexOf(button);
                if (at >= 0 && button.interactable && InputMode.Pointer) Focus(at);
            });
        }

        private string FolderOf(PictureKind kind) => kind switch
        {
            PictureKind.Standard => Art.ImagesFolder,
            PictureKind.Hd => Art.HdFolder,
            _ => Art.CardsFolder,
        };

        private string? OwnBack()
        {
            foreach (var name in new[] { "back.png", "back.jpg" })
            {
                var path = Path.Combine(Art.CardsFolder, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Each section's count, path and (Card Back) preview, as they are on disk now.</summary>
        private void RefreshRows()
        {
            var T = UiTheme.Current;
            foreach (var s in _sections)
            {
                string full;
                if (s.Kind == PictureKind.Back)
                {
                    var own = OwnBack();
                    s.Count.text = own != null ? "Your own card back" : "The game's card back";
                    s.Count.color = own != null ? T.Text : T.TextMuted;
                    full = own ?? Art.CardsFolder;
                    if (s.Preview != null) ShowPicture(s.Preview, Art.Back);
                }
                else if (s.Kind == PictureKind.Hd && !Art.ReadsHdFolder)
                {
                    // -hdpics off (a developer flag): the folder isn't read this run, so importing into it would show nothing.
                    s.Count.text = "Not used on this run (-hdpics off)";
                    s.Count.color = T.TextMuted;
                    full = FolderOf(s.Kind);
                }
                else
                {
                    int n = Art.CountIn(FolderOf(s.Kind));
                    s.Count.text = n == 0 ? "No pictures yet" : Count(n, "picture", "pictures");
                    s.Count.color = n == 0 ? T.TextMuted : T.Text;
                    full = FolderOf(s.Kind);
                }
                s.Path.text = ShortPath(s.Path, FullPath(full));
            }
        }

        private static string FullPath(string path)
        {
            try { return Path.GetFullPath(path); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return path; }
        }

        /// <summary>A picture fitted in a box without stretching it (null: the game's own card back, drawn).</summary>
        private static void ShowPicture(RectTransform box, Texture? texture)
        {
            for (int i = box.childCount - 1; i >= 0; i--)
            {
                box.GetChild(i).gameObject.SetActive(false);
                Destroy(box.GetChild(i).gameObject);
            }
            var holder = Ui.Stretched("Picture", box, Vector2.zero, Vector2.one);
            if (texture != null)
            {
                var raw = holder.gameObject.AddComponent<RawImage>();
                raw.texture = texture;
                var fit = holder.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = texture.width / (float)Mathf.Max(1, texture.height);
            }
            else CardView.DrawBack(holder);
            foreach (var g in holder.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        }

        // ---- Numbers and paths ----------------------------------------------------------------------------------

        public static string N(int n) => n.ToString("N0", Inv);
        public static string Count(int n, string one, string many) => $"{N(n)} {(n == 1 ? one : many)}";

        /// <summary>The widest a path line may be (its box is 1060; a little room so it never wraps).</summary>
        private const float PathWidth = 1056;

        /// <summary>
        /// A path on one line: whole when it fits, else its root (E:\, or ~/ for the home folder off Windows), "…" and as
        /// many of its last folders as fit, at least the last two; when even those don't fit, the left of them cut.
        /// </summary>
        public static string ShortPath(Text text, string full)
        {
            float Width(string s) => text.cachedTextGeneratorForLayout.GetPreferredWidth(s, text.GetGenerationSettings(Vector2.zero)) / Mathf.Max(0.0001f, text.pixelsPerUnit);
            if (Width(full) <= PathWidth) return full;
            var sep = Path.DirectorySeparatorChar.ToString();
            string root = Path.GetPathRoot(full) ?? "";
            string rest = full.Substring(root.Length);
            bool windows = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/', '\\');
            if (!windows && home.Length > 1 && full.StartsWith(home + "/", StringComparison.Ordinal))
            {
                root = "~/";
                rest = full.Substring(home.Length + 1);
            }
            var parts = rest.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            for (int k = parts.Length - 1; k >= 2; k--)
            {
                var s = root + "…" + sep + string.Join(sep, parts.Skip(parts.Length - k));
                if (Width(s) <= PathWidth) return s;
            }
            var tail = string.Join(sep, parts.Skip(Math.Max(0, parts.Length - 2)));
            // The fewest characters cut from the left of the last two that fit (the width only shrinks as more go).
            int lo = 0, hi = tail.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Width(root + "…" + tail.Substring(mid)) <= PathWidth) hi = mid;
                else lo = mid + 1;
            }
            return root + "…" + tail.Substring(lo);
        }

        // ---- The flow: pick, scan, ask, import, done --------------------------------------------------------------

        /// <summary>Import...: the file dialog, then the import (or the dialog's refusal on the status line).</summary>
        private void Pick(int section)
        {
            if (_state != State.Idle || !_sections[section].Import.interactable) return;
            bool back = _sections[section].Kind == PictureKind.Back;
            var request = new FilePicker.Request
            {
                Title = back ? "Choose a card back" : "Import card pictures",
                Prompt = "Import",
                FilterName = back ? "Pictures" : "Card pictures and zips",
                Extensions = back ? new[] { "jpg", "jpeg", "png" } : new[] { "zip", "jpg", "jpeg", "png" },
                Multiple = !back,
            };
            var paths = FilePicker.Pick(request, out var refused);
            _openedFrame = Time.frameCount; // the dialog swallowed the keys: nothing read on its frame
            if (paths == null || paths.Length == 0)
            {
                if (refused != null) SetStatus($"The file window can't open: {refused.TrimEnd('.')}.", UiTheme.Current.Bad);
                return;
            }
            Begin(section, paths);
        }

        /// <summary>
        /// For checks: imports <paramref name="paths"/> into a section by the same path as Import... minus the file dialog.
        /// <paramref name="replace"/>: the answer to the overwrite question (or, for the card back, Use this back / Cancel)
        /// when it comes; null leaves the question up for a press. False when something is already under way.
        /// </summary>
        public bool DebugImport(PictureKind kind, string[] paths, bool? replace)
        {
            int section = _sections.FindIndex(s => s.Kind == kind);
            if (_state != State.Idle || !IsOpen || !_sections[section].Import.interactable) return false;
            _debugAnswer = replace;
            Begin(section, paths);
            return true;
        }

        private void Begin(int section, string[] paths)
        {
            _section = section;
            _paths = paths.ToArray();
            _plan = null;
            SetStatus("", UiTheme.Current.TextSoft);
            var kind = _sections[section].Kind;
            var target = FolderOf(kind);
            var list = _paths;
            _activityLine.text = list.Length == 1 ? $"Reading {Path.GetFileName(list[0])}..." : $"Reading {Count(list.Length, "file", "files")}...";
            ShowActivity(importing: false);
            _scan = Task.Run(() => PictureImport.Scan(kind, list, target));
            SetState(State.Scanning, 0);
        }

        /// <summary>The scan's result: a problem, the card back's question, the overwrite question, or straight to importing.</summary>
        private void Scanned(PictureImportPlan plan)
        {
            _plan = plan;
            _activity.gameObject.SetActive(false);
            if (plan.Problem != null) ShowProblem(plan.Problem);
            else if (_sections[_section].Kind == PictureKind.Back) ShowBackConfirm(_paths[0]);
            else if (plan.Total == 0) ShowProblem("There are no card pictures in these files. Card pictures are named by card number, like 89631139.jpg.");
            else if (plan.Existing > 0) ShowQuestion(plan);
            else StartRun(false);
        }

        private void StartRun(bool replace)
        {
            var plan = _plan!;
            CloseModal();
            _counter = new Counter();
            _counter.Report((0, plan.Total));
            _cts = new CancellationTokenSource();
            _stopping = false;
            var counter = _counter;
            var token = _cts.Token;
            _activityLine.text = $"Importing into {_sections[_section].Heading}...";
            ShowActivity(importing: true);
            UpdateBar();
            _run = Task.Run(() => PictureImport.Run(plan, replace, counter, token));
            SetState(State.Importing, 0);
        }

        /// <summary>Cancel (the button, Esc, B) while reading or importing.</summary>
        private void CancelWork()
        {
            if (_state == State.Scanning)
            {
                DropScan();
                _activity.gameObject.SetActive(false);
                SetStatus("Import cancelled. Nothing was changed.", UiTheme.Current.TextSoft);
                SetState(State.Idle, IdleFocus());
            }
            else if (_state == State.Importing && !_stopping)
            {
                _stopping = true;
                _cts?.Cancel();
                _activityLine.text = "Stopping...";
                _activityCancel.interactable = false;
            }
        }

        /// <summary>A scan's result is ignored when it lands (Scan writes nothing, so there's nothing to undo).</summary>
        private void DropScan() => _scan = null;

        /// <summary>Cancel on a question: nothing written.</summary>
        private void Cancelled()
        {
            CloseModal();
            SetStatus("Import cancelled. Nothing was changed.", UiTheme.Current.TextSoft);
            SetState(State.Idle, IdleFocus());
        }

        private void ProblemOk()
        {
            CloseModal();
            SetState(State.Idle, IdleFocus());
        }

        /// <summary>The import ended (done, stopped or failed): pictures refreshed everywhere, the result on the status line.</summary>
        private void Finished(Task<PictureImportResult> task)
        {
            _run = null;
            _cts?.Dispose();
            _cts = null;
            var T = UiTheme.Current;
            var section = _sections[_section];
            if (task.IsFaulted || task.IsCanceled)
            {
                var why = task.Exception?.GetBaseException().Message ?? "it was stopped";
                Debug.LogWarning($"Import Cards: the import stopped: {task.Exception}");
                Art.Refresh(); // what was written before it stopped is on disk (the disk cache knows changed files by size and time)
                SetStatus($"The import stopped: {why.TrimEnd('.')}.", T.Bad);
            }
            else
            {
                var result = task.Result;
                if (result.WrittenFiles.Count + result.RemovedFiles.Count > 0)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Art.Refresh();
                    DebugRefreshMs = watch.Elapsed.TotalMilliseconds;
                }
                var (text, color) = Summary(section, _plan!, result);
                SetStatus(text, color);
            }
            _activity.gameObject.SetActive(false);
            SetState(State.Idle, _section * 2);
            RefreshRows();
        }

        /// <summary>The result line (UI spec 3f): only the parts that aren't zero, in a fixed order.</summary>
        private static (string text, Color color) Summary(Section section, PictureImportPlan plan, PictureImportResult r)
        {
            var T = UiTheme.Current;
            int written = r.Added + r.Replaced;
            string FirstWhy() => r.FailedExamples.Count > 0 ? r.FailedExamples[0] : "";
            if (section.Kind == PictureKind.Back)
            {
                if (r.Failed > 0)
                {
                    var example = FirstWhy();
                    int colon = example.IndexOf(": ", StringComparison.Ordinal);
                    return ($"The card back couldn't be saved: {(colon >= 0 ? example.Substring(colon + 2) : example).TrimEnd('.')}.", T.Warn);
                }
                if (r.Cancelled && written == 0) return ("Import cancelled. Nothing was changed.", T.TextSoft);
                return written > 0 ? ("Card back changed.", T.Good) : ("The card back is as it was.", T.TextSoft);
            }
            if (r.Cancelled)
                return ($"Import stopped: {N(written)} saved before it stopped. Nothing half-saved was left.", T.TextSoft);
            var parts = new List<string>();
            if (r.Added > 0) parts.Add($"{N(r.Added)} added");
            if (r.Replaced > 0) parts.Add($"{N(r.Replaced)} replaced");
            var rest = new List<string>();
            if (r.Kept > 0) rest.Add($"{N(r.Kept)} kept");
            if (plan.Skipped > 0) rest.Add($"{N(plan.Skipped)} skipped");
            if (r.Failed > 0) rest.Add($"{N(r.Failed)} couldn't be saved" + (r.FailedExamples.Count > 0 ? $" ({FirstWhy()})" : ""));
            var color = r.Failed > 0 ? T.Warn : written > 0 ? T.Good : T.TextSoft;
            if (written == 0)
                return ($"{section.Heading}: nothing new." + (rest.Count > 0 ? " " + string.Join(", ", rest) + "." : ""), color);
            parts.AddRange(rest);
            return ($"{section.Heading}: {string.Join(", ", parts)}.", color);
        }

        /// <summary>Open folder: the section's folder in Explorer or Finder (made first if missing).</summary>
        private void OpenFolder(int section)
        {
            if (_state != State.Idle) return;
            var folder = FolderOf(_sections[section].Kind);
            try { Directory.CreateDirectory(folder); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                SetStatus($"{FullPath(folder)} can't be made: {e.Message}", UiTheme.Current.Bad);
                return;
            }
            var full = FullPath(folder);
            var url = new Uri(full.TrimEnd('/', '\\') + Path.DirectorySeparatorChar).AbsoluteUri;
            SetStatus($"Opened {full}", UiTheme.Current.TextMuted);
            if (DuelController.IsCheckRun)
            {
                DebugOpened.Add(url);
                _status.text += " (not opened: automated check)";
                return;
            }
            Application.OpenURL(url);
        }

        /// <summary>For checks: what Open folder would have opened (a check run never opens a folder).</summary>
        public readonly List<string> DebugOpened = new List<string>();

        private void SetStatus(string text, Color color)
        {
            _status.text = text;
            _status.color = color;
        }

        private void ShowActivity(bool importing)
        {
            _activity.gameObject.SetActive(true);
            _activityNote.gameObject.SetActive(!importing);
            _barTrack.gameObject.SetActive(importing);
            _activityCount.gameObject.SetActive(importing);
            _activityCancel.interactable = true;
        }

        private void UpdateBar()
        {
            if (_counter == null) return;
            int done = _counter.Done, total = _counter.Total;
            float part = total > 0 ? Mathf.Clamp01(done / (float)total) : 0;
            _barFill.anchorMax = new Vector2(part, 1);
            _barFill.offsetMax = Vector2.zero;
            _activityCount.text = $"{N(done)} of {N(total)}";
        }

        // ---- The modals -----------------------------------------------------------------------------------------

        private RectTransform OpenModal(float x, float y, float w, float h)
        {
            CloseModal();
            var T = UiTheme.Current;
            _modal = Ui.Stretched("Modal", _root, Vector2.zero, Vector2.one);
            _modal.SetAsLastSibling();
            Ui.Image(_modal, T.Scrim.WithAlpha(0.75f)); // covers the page and its clicks; clicking it does nothing
            var panel = Ui.Box(_modal, x, y, w, h);
            Ui.Image(panel, T.PanelDeep);
            _layout.Add(("panel", "modal", panel));
            _openedFrame = Time.frameCount;
            return _modal;
        }

        private void CloseModal()
        {
            if (_newBack != null)
            {
                // The preview showing it goes with the modal, this frame.
                if (_modal != null) _modal.gameObject.SetActive(false);
                Destroy(_newBack);
                _newBack = null;
            }
            if (_modal == null) return;
            // The keys' frame out first: destroying the modal with the frame under it left the frame's image disabled when
            // it was moved on (it then never drew on the next modal).
            if (_frame != null && _frame.IsChildOf(_modal))
            {
                _frame.SetParent(_main, false);
                _frame.gameObject.SetActive(false);
            }
            _layout.RemoveAll(l => l.group == "modal");
            _primary.RemoveWhere(b => b == null || b.transform.IsChildOf(_modal));
            _modal.gameObject.SetActive(false);
            Destroy(_modal.gameObject);
            _modal = null;
        }

        private Text ModalText(string what, float x, float y, float w, float h, string text, int size, Color color, bool bold = false,
            TextAnchor anchor = TextAnchor.UpperLeft) =>
            Label(_modal!, "modal", what, x, y, w, h, text, size, color, bold, anchor);

        private Button ModalButton(float x, float y, float w, string label, Action action, bool primary = false)
        {
            var button = Ui.BoxButton(_modal!, x, y, w, 56, label, action, 22);
            if (primary)
            {
                button.targetGraphic.color = UiTheme.Current.Primary;
                _primary.Add(button);
            }
            _layout.Add((label, "modal", (RectTransform)button.transform));
            Hover(button);
            return button;
        }

        /// <summary>The overwrite question (Images / HD, when pictures already exist): Keep mine is the default and the accent.</summary>
        private void ShowQuestion(PictureImportPlan plan)
        {
            var T = UiTheme.Current;
            OpenModal(460, 300, 1000, 440);
            var title = plan.Existing == 1 ? "1 of these pictures already exists."
                : plan.Existing == plan.Total ? $"All {N(plan.Existing)} of these pictures already exist."
                : $"{N(plan.Existing)} of these pictures already exist.";
            ModalText("title", 500, 330, 920, 44, title, 30, T.Text, bold: true, anchor: TextAnchor.MiddleLeft);
            var lines = new List<string> { $"New: {N(plan.New)}", $"Already there: {N(plan.Existing)}" };
            if (plan.Skipped > 0) lines.Add($"Skipped: {N(plan.Skipped)}");
            ModalText("counts", 500, 390, 920, 96, string.Join("\n", lines), 20, T.TextSoft);
            if (plan.Skipped > 0)
                ModalText("examples", 500, 494, 920, 52, "Skipped because: " + string.Join("; ", plan.SkippedExamples.Take(2)), 16, T.TextMuted);
            ModalText("ask", 500, 556, 920, 56, "Replace them with the new ones, or keep yours?", 20, T.TextSoft);
            var replace = ModalButton(500, 660, 260, "Replace them", () => StartRun(true));
            var keep = ModalButton(780, 660, 240, "Keep mine", () => StartRun(false), primary: true);
            var cancel = ModalButton(1040, 660, 200, "Cancel", Cancelled);
            SetState(State.Question, 1, replace, keep, cancel);
            if (_debugAnswer is bool answer)
            {
                _debugAnswer = null;
                StartRun(answer);
            }
        }

        /// <summary>The whole import can't run: its reason and OK.</summary>
        private void ShowProblem(string problem)
        {
            var T = UiTheme.Current;
            OpenModal(560, 360, 800, 300);
            ModalText("title", 600, 390, 720, 44, "That didn't work", 30, T.Text, bold: true, anchor: TextAnchor.MiddleLeft);
            ModalText("message", 600, 446, 720, 110, problem, 20, T.TextSoft);
            var ok = ModalButton(600, 580, 200, "OK", ProblemOk);
            _debugAnswer = null;
            SetState(State.Problem, 0, ok);
        }

        /// <summary>The card back, always asked: the one now and the new one side by side.</summary>
        private void ShowBackConfirm(string path)
        {
            var T = UiTheme.Current;
            OpenModal(560, 200, 800, 620);
            ModalText("title", 600, 230, 720, 44, "Use this card back?", 30, T.Text, bold: true, anchor: TextAnchor.MiddleLeft);
            var now = Ui.Box(_modal!, 713, 300, 207, 300);
            _layout.Add(("old preview", "modal", now));
            ShowPicture(now, Art.Back);
            ModalText("arrow", 920, 420, 80, 60, "→", 40, T.Gold, anchor: TextAnchor.MiddleCenter);
            var fresh = Ui.Box(_modal!, 1000, 300, 207, 300);
            _layout.Add(("new preview", "modal", fresh));
            try
            {
                _newBack = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "new card back (preview)" };
                if (!_newBack.LoadImage(File.ReadAllBytes(path))) { Destroy(_newBack); _newBack = null; }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (_newBack != null) Destroy(_newBack);
                _newBack = null;
            }
            if (_newBack != null) ShowPicture(fresh, _newBack);
            else Ui.Text(fresh, "(no preview)", 16, T.TextMuted);
            ModalText("caption now", 683, 610, 267, 28, "Now", 18, T.TextMuted, anchor: TextAnchor.MiddleCenter);
            ModalText("caption new", 970, 610, 267, 28, "New", 18, T.TextMuted, anchor: TextAnchor.MiddleCenter);
            ModalText("note", 600, 650, 720, 60, OwnBack() != null ? "Your own card back will be replaced."
                : "The game's card back stays in the game; this one is used instead.", 18, T.TextSoft);
            var use = ModalButton(600, 740, 260, "Use this back", () => StartRun(true), primary: true);
            var cancel = ModalButton(880, 740, 200, "Cancel", Cancelled);
            SetState(State.BackConfirm, 0, use, cancel);
            if (_debugAnswer is bool answer)
            {
                _debugAnswer = null;
                if (answer) StartRun(true);
                else Cancelled();
            }
        }

        // ---- State, focus and keys ------------------------------------------------------------------------------

        /// <summary>The idle screen's highlight: the last section's Import... (Card Images' at first).</summary>
        private int IdleFocus() => _section * 2;

        /// <summary>
        /// Enters a state: which buttons the keys move over (the page's while idle; else <paramref name="buttons"/>, or Cancel
        /// while reading or importing), the page's buttons off while anything is up, and the highlight.
        /// </summary>
        private void SetState(State state, int focus, params Button[] buttons)
        {
            _state = state;
            bool idle = state == State.Idle;
            foreach (var s in _sections)
            {
                s.Open.interactable = idle;
                s.Import.interactable = idle && (s.Kind != PictureKind.Hd || Art.ReadsHdFolder);
            }
            _back.interactable = idle;
            _keys.Clear();
            if (idle) foreach (var s in _sections) { _keys.Add(s.Import); _keys.Add(s.Open); }
            if (idle) _keys.Add(_back);
            else if (state == State.Scanning || state == State.Importing) _keys.Add(_activityCancel);
            else _keys.AddRange(buttons);
            Focus(focus);
        }

        private void Focus(int index)
        {
            if (_keys.Count == 0) return;
            _focus = Mathf.Clamp(index, 0, _keys.Count - 1);
            ArrowNav.Mark(ref _frame, _main, (RectTransform)_keys[_focus].transform, 6);
        }

        /// <summary>An arrow from the highlight: a grid on the idle page (rows Images, HD, Back; then Back); a line elsewhere.</summary>
        private void Step(Vector2Int move)
        {
            if (_state != State.Idle)
            {
                int step = move.x != 0 ? move.x : move.y;
                Focus(_focus + step);
                return;
            }
            const int backButton = 6;
            if (_focus == backButton)
            {
                if (move.y < 0) Focus(4); // up: the Card Back row's Import...
                return;
            }
            int row = _focus / 2, col = _focus % 2;
            if (move.x != 0) Focus(row * 2 + Mathf.Clamp(col + move.x, 0, 1));
            else if (move.y > 0) Focus(row < 2 ? (row + 1) * 2 + col : backButton);
            else if (move.y < 0 && row > 0) Focus((row - 1) * 2 + col);
        }

        private void Update()
        {
            Poll(); // work finishing lands even if the screen was closed under it
            if (!IsOpen || Time.frameCount == _openedFrame || _keys.Count == 0) return; // the press that opened it isn't also read here
            var keys = _nav.Read(_root, (RectTransform)_keys[_focus].transform);
            bool escape = false;
#if ENABLE_INPUT_SYSTEM
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
                if (device is UnityEngine.InputSystem.Keyboard k) escape |= k.escapeKey.wasPressedThisFrame;
#else
            escape = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (escape && !ArrowNav.TypingSomewhere()) { Back(); return; }
            if (keys.Move != Vector2Int.zero) Step(keys.Move);
            if (keys.Enter && _keys[_focus].interactable) _keys[_focus].onClick.Invoke();
        }

        /// <summary>The background work: a finished scan moves on; an import shows its progress, then its result.</summary>
        private void Poll()
        {
            if (_root == null) return;
            if (_state == State.Scanning && _scan != null && _scan.IsCompleted)
            {
                var task = _scan;
                _scan = null;
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.LogWarning($"Import Cards: the scan failed: {task.Exception}");
                    _activity.gameObject.SetActive(false);
                    ShowProblem($"The files can't be read: {(task.Exception?.GetBaseException().Message ?? "unknown error").TrimEnd('.')}.");
                }
                else Scanned(task.Result);
            }
            if (_run != null)
            {
                UpdateBar();
                if (_run.IsCompleted) Finished(_run);
            }
        }

        // ---- For checks -----------------------------------------------------------------------------------------

        public State DebugState => _state;
        /// <summary>For checks: how long the last import's <see cref="CardArt.Refresh"/> took (ms; -1 before one).</summary>
        public double DebugRefreshMs { get; private set; } = -1;

        /// <summary>For checks: the frame's state, to say why it isn't seen.</summary>
        public string DebugFrameInfo
        {
            get
            {
                if (_frame == null || _keys.Count == 0) return "no frame";
                var image = _frame.GetComponent<Image>();
                var target = (RectTransform)_keys[_focus].transform;
                var siblings = string.Join(",", Enumerable.Range(0, _frame.parent.childCount).Select(i => _frame.parent.GetChild(i).name));
                return $"frame {_frame.GetSiblingIndex()} target {target.GetSiblingIndex()} of [{siblings}]; colour {image.color}, enabled {image.enabled}, "
                       + $"alpha {image.canvasRenderer.GetInheritedAlpha():0.00}, cull {image.canvasRenderer.cull}, scale {_frame.lossyScale}, target scale {target.lossyScale}, "
                       + $"pos {_frame.position} target {target.position}, depth {image.depth} target depth {target.GetComponent<Graphic>().depth}";
            }
        }

        /// <summary>For checks: whether the gold frame is drawn round the highlighted button (its rect grown by 6 on each side).</summary>
        public bool DebugFrameOnFocus
        {
            get
            {
                if (!DebugFrameShown || _keys.Count == 0) return false;
                var target = (RectTransform)_keys[_focus].transform;
                var frame = _frame!;
                if (!(frame.GetComponent<Image>() is Image image) || !image.enabled || image.color.a < 0.5f) return false;
                if (frame.parent != target.parent || frame.GetSiblingIndex() > target.GetSiblingIndex()) return false;
                var t = target.rect;
                var f = frame.rect;
                Vector2 Center(RectTransform rt) => rt.parent.InverseTransformPoint(rt.TransformPoint(rt.rect.center));
                return Mathf.Abs(f.width - t.width - 12) < 0.5f && Mathf.Abs(f.height - t.height - 12) < 0.5f
                    && (Center(frame) - Center(target)).sqrMagnitude < 0.25f;
            }
        }
        public string DebugStatus => _status != null ? _status.text : "";
        public Color DebugStatusColor => _status != null ? _status.color : Color.clear;
        public string DebugActivityLine => _activityLine != null && _activity.gameObject.activeSelf ? _activityLine.text : "";
        public (int done, int total) DebugProgress => _counter != null ? (_counter.Done, _counter.Total) : (0, 0);
        public PictureImportPlan? DebugPlan => _plan;

        /// <summary>For checks: the highlighted button's label.</summary>
        public string DebugFocusLabel => _keys.Count > 0 ? _keys[_focus].GetComponentInChildren<Text>().text : "";

        /// <summary>For checks: the highlighted button's section ("" off the rows).</summary>
        public string DebugFocusSection => _state == State.Idle && _focus < 6 ? _sections[_focus / 2].Heading : "";

        /// <summary>For checks: whether the keyboard's gold frame shows (highlight mode).</summary>
        public bool DebugFrameShown => _frame != null && _frame.gameObject.activeInHierarchy && _frame.GetComponent<KeyFrame>() is KeyFrame f && f.Shown;

        /// <summary>For checks: a section's count line and path line as shown.</summary>
        public (string count, string path) DebugRow(PictureKind kind)
        {
            var s = _sections.First(x => x.Kind == kind);
            return (s.Count.text, s.Path.text);
        }

        /// <summary>For checks: the path line of a section showing <paramref name="fullPath"/> (shortened as a real one would be).</summary>
        public Text DebugPathLine(PictureKind kind, string fullPath)
        {
            var s = _sections.First(x => x.Kind == kind);
            s.Path.text = ShortPath(s.Path, fullPath);
            return s.Path;
        }

        /// <summary>For checks: the rows as they are on disk again (after <see cref="DebugPathLine"/>).</summary>
        public void DebugRefreshRows() => RefreshRows();

        /// <summary>For checks: presses a button the keys can reach now, by its label, as a click would.</summary>
        public bool DebugPress(string label, PictureKind? section = null)
        {
            Button? b = section is PictureKind kind
                ? (label == "Import..." ? _sections.First(s => s.Kind == kind).Import : _sections.First(s => s.Kind == kind).Open)
                : _keys.FirstOrDefault(k => k.GetComponentInChildren<Text>().text == label);
            if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) return false;
            b.onClick.Invoke();
            return true;
        }

        /// <summary>For checks: a button's rect (for a mouse click).</summary>
        public RectTransform? DebugButton(string label, PictureKind? section = null)
        {
            if (section is PictureKind kind)
            {
                var s = _sections.First(x => x.Kind == kind);
                return (RectTransform)(label == "Import..." ? s.Import : s.Open).transform;
            }
            var b = _keys.FirstOrDefault(k => k.GetComponentInChildren<Text>().text == label);
            return b != null ? (RectTransform)b.transform : null;
        }

        /// <summary>For checks: the labels the keys move over now, and whether each can be pressed.</summary>
        public List<(string label, bool on)> DebugKeys => _keys.Select(k => (k.GetComponentInChildren<Text>().text, k.interactable)).ToList();

        /// <summary>For checks: whether the page's own buttons (the rows' and Back) can be pressed.</summary>
        public bool DebugPageButtonsOn => _back.interactable && _sections.All(s => s.Import.interactable && s.Open.interactable);

        /// <summary>For checks: whether a section's Import... can be pressed.</summary>
        public bool DebugImportOn(PictureKind kind) => _sections.First(s => s.Kind == kind).Import.interactable;

        /// <summary>For checks: the accent (Primary) buttons showing, on the page or a modal.</summary>
        public List<string> DebugPrimary => _primary.Where(b => b != null && b.gameObject.activeInHierarchy)
            .Select(b => b.GetComponentInChildren<Text>().text).ToList();

        /// <summary>For checks: every text showing (the page, the activity panel, a modal).</summary>
        public List<Text> DebugTexts() => _root.GetComponentsInChildren<Text>().Where(t => t.isActiveAndEnabled).ToList();

        /// <summary>
        /// For checks: the boxes showing, in the 1920x1080 layout from its top-left, each with its group: "page" (the title, the
        /// privacy line, the three rows, the activity panel, the status line, Back, the hint), a row's ("images row"...),
        /// "activity", or "modal" (its "panel" first).
        /// </summary>
        public List<(string what, string group, Rect rect)> DebugBoxes()
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
            return _layout.Where(l => l.rt != null && l.rt.gameObject.activeInHierarchy).Select(l => (l.what, l.group, Of(l.rt))).ToList();
        }

        /// <summary>For checks: the texture the Card Back row shows (null: the game's own back, drawn).</summary>
        public Texture? DebugRowPreview => _sections.First(s => s.Kind == PictureKind.Back).Preview?.GetComponentInChildren<RawImage>()?.texture;
    }
}
