using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Replays;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Replay files in mods/replays: ones you saved, plus the last <see cref="KeepRecent"/> finished duels in
    /// mods/replays/recent (saved automatically). Each file holds a replay code, so it can be pasted anywhere.
    /// </summary>
    public sealed class ReplayLibrary
    {
        public const int KeepRecent = 30;
        private const string Extension = ".replay";

        public string SavedFolder { get; }
        public string RecentFolder { get; }

        public ReplayLibrary(string modsRoot)
        {
            SavedFolder = Path.Combine(modsRoot, "replays");
            RecentFolder = Path.Combine(SavedFolder, "recent");
            Directory.CreateDirectory(RecentFolder);
        }

        public sealed class Entry
        {
            public string Path = "";
            public bool Recent;
            public Replay Replay = null!;
        }

        /// <summary>Saves a replay you chose to keep; returns the file path.</summary>
        public string Save(Replay replay) => Write(SavedFolder, replay);

        /// <summary>Saves a finished duel to the recent list, dropping the oldest beyond <see cref="KeepRecent"/>.</summary>
        public void SaveRecent(Replay replay)
        {
            Write(RecentFolder, replay);
            foreach (var old in Directory.GetFiles(RecentFolder, "*" + Extension).OrderByDescending(File.GetLastWriteTimeUtc).Skip(KeepRecent))
                File.Delete(old);
        }

        /// <summary>Moves a recent replay into the saved ones so it isn't rotated out.</summary>
        public void Keep(Entry entry)
        {
            if (!entry.Recent) return;
            var target = UniquePath(SavedFolder, System.IO.Path.GetFileNameWithoutExtension(entry.Path));
            File.Move(entry.Path, target);
        }

        /// <summary>Every readable replay, newest first.</summary>
        public List<Entry> All()
        {
            var entries = new List<Entry>();
            foreach (var (folder, recent) in new[] { (SavedFolder, false), (RecentFolder, true) })
                foreach (var file in Directory.GetFiles(folder, "*" + Extension))
                {
                    Replay? replay = null;
                    try { replay = Replay.Load(file); } catch (IOException) { }
                    if (replay != null) entries.Add(new Entry { Path = file, Recent = recent, Replay = replay });
                }
            return entries.OrderByDescending(e => e.Replay.Date).ToList();
        }

        private static string Write(string folder, Replay replay)
        {
            var name = $"{replay.Date:yyyy-MM-dd HHmm} {replay.Names[0]} vs {replay.Names[1]}";
            foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var path = UniquePath(folder, name);
            replay.Save(path);
            return path;
        }

        private static string UniquePath(string folder, string name)
        {
            var path = System.IO.Path.Combine(folder, name + Extension);
            for (int i = 2; File.Exists(path); i++) path = System.IO.Path.Combine(folder, $"{name} ({i}){Extension}");
            return path;
        }
    }

    /// <summary>The replay list: watch, copy a replay's code, keep a recent one, or paste a code to watch.</summary>
    public sealed class ReplayBrowser : MonoBehaviour
    {
        private const int PerPage = 9;

        private ReplayLibrary _library = null!;
        private RectTransform _root = null!;
        private RectTransform _list = null!;
        private InputField _code = null!;
        private Text _status = null!;
        private Text _pageLabel = null!;
        private List<ReplayLibrary.Entry> _entries = new List<ReplayLibrary.Entry>();
        private int _page;

        /// <summary>A replay to watch (the browser has closed itself).</summary>
        public event Action<Replay>? Watch;
        /// <summary>Closed without picking a replay to watch.</summary>
        public event Action? Closed;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;

        public void Init(ReplayLibrary library)
        {
            _library = library;
            Build();
            _root.gameObject.SetActive(false);
        }

        public void Open()
        {
            _root.gameObject.SetActive(true);
            _status.text = $"Your last {ReplayLibrary.KeepRecent} finished duels are kept automatically; Keep one to hold on to it.";
            Refresh();
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>Closes to watch a replay (no Closed event: the replay takes over the screen).</summary>
        private void CloseForWatching() => _root.gameObject.SetActive(false);

        private void Build()
        {
            var canvasGo = new GameObject("ReplayCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20; // above the duel and the deck editor
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the whole 1920x1080 layout stays visible at any aspect ratio

            _root = Ui.Stretched("Replays", canvasGo.transform, Vector2.zero, Vector2.one);
            Ui.Image(_root, new Color(0, 0, 0, 0.7f));
            var panel = Ui.Placed("Panel", _root, Vector2.zero, new Vector2(1240, 860));
            Ui.Image(panel, Ui.Hex("#1A1F2B"));

            Ui.Text(Ui.Placed("Title", panel, new Vector2(-470, 395), new Vector2(260, 40)), "REPLAYS", 24, Ui.Hex("#FFC940"), TextAnchor.MiddleLeft)
                .fontStyle = FontStyle.Bold;
            Ui.Button(panel, "Close", new Vector2(560, 395), new Vector2(100, 40), Close);

            // Paste a code someone shared.
            _code = Ui.Input(Ui.Placed("Code", panel, new Vector2(-150, 340), new Vector2(880, 40)), "Paste a replay code (YGOR1:...)", 15,
                onSubmit: _ => WatchCode(_code.text));
            Ui.Button(panel, "Paste", new Vector2(365, 340), new Vector2(120, 40), () =>
            {
                _code.text = GUIUtility.systemCopyBuffer;
                WatchCode(_code.text);
            });
            Ui.Button(panel, "Watch code", new Vector2(515, 340), new Vector2(150, 40), () => WatchCode(_code.text));

            _status = Ui.Text(Ui.Placed("Status", panel, new Vector2(0, 295), new Vector2(1180, 30)), "", 14,
                new Color(0.75f, 0.78f, 0.85f), TextAnchor.MiddleLeft);
            _list = Ui.Placed("List", panel, new Vector2(0, -20), new Vector2(1180, 9 * 64));

            Ui.Button(panel, "< Prev", new Vector2(-200, -380), new Vector2(120, 40), () => { _page--; ShowPage(); });
            _pageLabel = Ui.Text(Ui.Placed("Page", panel, new Vector2(0, -380), new Vector2(200, 40)), "", 16, Color.white);
            Ui.Button(panel, "Next >", new Vector2(200, -380), new Vector2(120, 40), () => { _page++; ShowPage(); });
        }

        private void Refresh()
        {
            _entries = _library.All();
            ShowPage();
        }

        private void ShowPage()
        {
            for (int i = _list.childCount - 1; i >= 0; i--) Destroy(_list.GetChild(i).gameObject);
            int pages = Math.Max(1, (_entries.Count + PerPage - 1) / PerPage);
            _page = Mathf.Clamp(_page, 0, pages - 1);
            _pageLabel.text = $"{_page + 1} / {pages}";
            if (_entries.Count == 0)
            {
                Ui.Text(Ui.Placed("Empty", _list, Vector2.zero, new Vector2(1000, 60)),
                    "No replays yet. Finish a duel, or press Save replay during one.", 18, new Color(1, 1, 1, 0.6f));
                return;
            }
            for (int row = 0; row < PerPage; row++)
            {
                int i = _page * PerPage + row;
                if (i >= _entries.Count) break;
                var entry = _entries[i];
                var y = 4 * 64 - row * 64;
                var rt = Ui.Placed("Row", _list, new Vector2(0, y), new Vector2(1180, 58));
                Ui.Image(rt, row % 2 == 0 ? Ui.Hex("#20273A") : Ui.Hex("#1C2232"));
                var r = entry.Replay;
                Ui.Text(Ui.Placed("Name", rt, new Vector2(-235, 10), new Vector2(690, 26)), TitleOf(r), 16, Color.white, TextAnchor.MiddleLeft);
                var tag = entry.Recent ? "recent" : "<color=#FFC940>saved</color>";
                var human = r.HumanSeat >= 0 ? " · you played" : "";
                var meta = Ui.Text(Ui.Placed("Meta", rt, new Vector2(-235, -14), new Vector2(690, 22)),
                    $"{r.Date:ddd d MMM yyyy, HH:mm} · {tag}{human} · {r.Responses.Count} moves", 13, new Color(0.7f, 0.74f, 0.82f), TextAnchor.MiddleLeft);
                meta.supportRichText = true;
                Ui.Button(rt, "Watch", new Vector2(215, 0), new Vector2(110, 40), () =>
                {
                    CloseForWatching();
                    Watch?.Invoke(r);
                });
                Ui.Button(rt, "Copy code", new Vector2(345, 0), new Vector2(130, 40), () => CopyCode(r));
                if (entry.Recent)
                    Ui.Button(rt, "Keep", new Vector2(495, 0), new Vector2(130, 40), () =>
                    {
                        _library.Keep(entry);
                        _status.text = "Kept: it won't be rotated out of the recent list.";
                        Refresh();
                    });
            }
        }

        /// <summary>Replay.Title with deck names in words ("chaos-yata" reads "Chaos Yata").</summary>
        private static string TitleOf(Replay r)
        {
            string Team(int t) => r.IsTag ? $"{r.Names[t]} & {r.PartnerNames![t]}" : $"{r.Names[t]} ({CampaignState.SlugTitle(r.DeckNames[t])})";
            var result = r.Winner == 2 ? "draw" : r.Winner >= 0 ? (r.IsTag ? $"{r.Names[r.Winner]} & {r.PartnerNames![r.Winner]} won" : $"{r.Names[r.Winner]} won") : "unfinished";
            return $"{Team(0)} vs {Team(1)} · {result}, turn {r.Turns}";
        }

        private void CopyCode(Replay replay)
        {
            var code = replay.ToCode();
            GUIUtility.systemCopyBuffer = code;
            _status.text = $"Copied the replay code ({code.Length} characters). Paste it anywhere; others watch it with Replays > Paste.";
        }

        private void WatchCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var replay = Replay.FromCode(text);
            if (replay == null)
            {
                _status.text = "<color=#FF8A8A>That isn't a replay code. Codes start with YGOR1:</color>";
                _status.supportRichText = true;
                return;
            }
            CloseForWatching();
            Watch?.Invoke(replay);
        }
    }
}
