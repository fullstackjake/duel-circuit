using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Data;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-importcheck &lt;folder&gt;` (absolute; with -mods &lt;empty folder&gt;): Import Cards (docs/dev/CARD-IMPORT-SPEC-2026-11-12.md,
    /// Part D). It makes its own test pictures in &lt;folder&gt;/pictures: flat colours (never card pictures), named after real
    /// passcodes from the card data, and zips of them; everything it writes goes there or into the -mods folder.
    /// <list type="number">
    /// <item>The menu: nine options in order, Import Cards between Settings and About, the geometry of the UI spec, every
    /// title and subtitle on one line; Import Cards shut over a campaign duel.</item>
    /// <item>The screen: opened with the keys, the layout rules (inside the canvas, no overlaps, children inside their
    /// panels, card-shaped previews, no clipped text, a 2000-character path on one line), focus defaults, the grid of keys.</item>
    /// <item>Import...'s dialog refuses in a check run (said on the status line); Open folder opens nothing.</item>
    /// <item>A zip into Card Images, a loose picture with a badly named file (skipped), a bad name alone (the problem), the
    /// same zip again with Keep mine, a blue zip with Replace them (the replaced cards show the new picture, though the old
    /// one was loaded and in the disk cache under the same path), HD (its folder read once even when settings.txt names it;
    /// a full 6,000-picture import and how long the refresh after it takes; Cancel during one), a card back (png, then
    /// jpg), Cancel while reading and on the question, FilePicker's pure helpers, -hdpics off, and Esc / B back to the menu.</item>
    /// </list>
    /// </summary>
    public sealed partial class DuelController
    {
        private IEnumerator ImportCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"import check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"import-{name}.png"));
                yield return null;
                yield return null;
            }
            IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
            IEnumerator Until(Func<bool> done, float seconds)
            {
                var end = Time.realtimeSinceStartup + seconds;
                while (!done() && Time.realtimeSinceStartup < end) yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
#if !ENABLE_INPUT_SYSTEM
            Report("the import check needs the Input System (keys are pressed through it)", false);
            Application.Quit();
            yield break;
#else
            var art = CardView.Art!;
            // The disk cache in the check's own folder (the game's is shared with real play): nothing written elsewhere.
            var artCache = Path.Combine(folder, "art-cache");
            ArtLoader.CacheFolder = artCache;
            var keyboard = InputSystem.AddDevice<Keyboard>("ImportCheckKeys");
            keyboard.MakeCurrent(); // the menu reads Keyboard.current
            IEnumerator Press(Key key)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                yield return Frames(3);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return Frames(3);
            }
            bool Idle() => _import.DebugState == ImportScreen.State.Idle;
            string Texts() => string.Join("\n", _import.DebugTexts().Select(t => t.text));
            // A modal's highlight: an arrow press that ends where it started puts the keys in charge (a mouse moved over the
            // window, by a person at the PC, hides the frame until then); the gold frame must then be round that button.
            IEnumerator FrameCheck(string where, params Key[] nudge)
            {
                Debug.Log($"import check: note {where}: before any key, pointer mode {InputMode.Pointer}, frame on the highlight {_import.DebugFrameOnFocus}");
                var label = _import.DebugFocusLabel;
                foreach (var key in nudge) yield return Press(key);
                yield return Frames(2);
                Debug.Log($"import check: note {where}: {_import.DebugFrameInfo}");
                Report($"{where}: after {string.Join(", ", nudge)} the gold frame is drawn round the highlighted button ({_import.DebugFocusLabel})",
                    _import.DebugFrameOnFocus && _import.DebugFocusLabel == label);
            }

            // ---- Test pictures: flat colours, never card pictures, named after real passcodes ----
            var pics = Path.Combine(folder, "pictures");
            if (Directory.Exists(pics)) Directory.Delete(pics, true);
            Directory.CreateDirectory(pics);
            var codes = _cardPack!.All.Where(c => c.Alias == 0 && c.Code >= 10000).Select(c => c.Code).Distinct().OrderBy(c => c)
                .Where(c => art.Get(c) == null).Take(6).ToList();
            Report($"six real passcodes with no picture yet: {string.Join(", ", codes)}", codes.Count == 6);
            if (codes.Count < 6) { Debug.Log($"import check: done, {bad} failed"); Application.Quit(1); yield break; }
            Color red = new Color(0.9f, 0.1f, 0.1f), blue = new Color(0.1f, 0.2f, 0.9f), green = new Color(0.1f, 0.8f, 0.2f),
                purple = new Color(0.6f, 0.1f, 0.8f), yellow = new Color(0.95f, 0.85f, 0.1f), cyan = new Color(0.1f, 0.85f, 0.9f);
            byte[] Picture(Color c, int w, int h, bool jpg = false)
            {
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var px = new Color32[w * h];
                Color32 c32 = c;
                for (int i = 0; i < px.Length; i++) px[i] = c32;
                t.SetPixels32(px);
                t.Apply();
                var bytes = jpg ? t.EncodeToJPG(95) : t.EncodeToPNG();
                Destroy(t);
                return bytes;
            }
            string Loose(string name, byte[] bytes)
            {
                var path = Path.Combine(pics, name);
                File.WriteAllBytes(path, bytes);
                return path;
            }
            string Zip(string name, IEnumerable<(string entry, byte[] data)> entries)
            {
                var path = Path.Combine(pics, name);
                using (var file = File.Create(path))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
                    foreach (var (entry, data) in entries)
                    {
                        using var s = zip.CreateEntry(entry, System.IO.Compression.CompressionLevel.Fastest).Open();
                        s.Write(data, 0, data.Length);
                    }
                return path;
            }
            // 276x400: card-shaped and tall enough to be compressed and kept in the disk cache (ArtLoader.CompressFrom).
            byte[] Card(Color c, bool jpg = false) => Picture(c, 276, 400, jpg);
            var redZip = Zip("red-cards.zip", new[]
            {
                ($"pics/{codes[0]}.png", Card(red)), ($"pics/{codes[1]}.png", Card(red)), ($"pics/{codes[2]}.png", Card(red)),
                ($"pics/{codes[3]}.jpg", Card(red, jpg: true)), ($"__MACOSX/pics/._{codes[0]}.png", new byte[] { 0, 5, 22, 7 }),
                ("pics/notes.txt", System.Text.Encoding.UTF8.GetBytes("test notes, not a picture")),
            });
            var blueZip = Zip("blue-cards.zip", Enumerable.Range(0, 4).Select(i => ($"pics/{codes[i]}.png", Card(blue)))
                .Append(("pics/notes.txt", System.Text.Encoding.UTF8.GetBytes("test notes, not a picture"))));
            var greenLoose = Loose($"{codes[4]}.png", Card(green));
            var cover = Loose("cover.png", Card(green));
            var hdLoose = Loose($"{codes[5]}.png", Picture(purple, 552, 800));
            var yellowBack = Loose("my-back.png", Picture(yellow, 345, 500));
            var cyanBack = Loose("other-back.jpg", Picture(cyan, 345, 500, jpg: true));
            var tiny = Picture(green, 8, 8);
            var bigZip = Zip("many-cards.zip", Enumerable.Range(0, 6000).Select(i => ($"{900000000 + i}.png", tiny)));

            // The middle pixel of a loaded picture (compressed ones too), through a render texture.
            Color Middle(Texture t)
            {
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(t, rt);
                var before = RenderTexture.active;
                RenderTexture.active = rt;
                var read = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                read.ReadPixels(new Rect(t.width / 2, t.height / 2, 1, 1), 0, 0);
                read.Apply();
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
                var c = read.GetPixel(0, 0);
                Destroy(read);
                return c;
            }
            bool Near(Texture? t, Color want)
            {
                if (t == null) return false;
                var c = Middle(t);
                return Mathf.Abs(c.r - want.r) < 0.2f && Mathf.Abs(c.g - want.g) < 0.2f && Mathf.Abs(c.b - want.b) < 0.2f;
            }
            string Rgb(Texture? t) { if (t == null) return "none"; var c = Middle(t); return $"{c.r:0.00},{c.g:0.00},{c.b:0.00}"; }

            // ---- The layout rules (UI spec section 6), on whatever shows now ----
            void Layout(string what)
            {
                Canvas.ForceUpdateCanvases();
                var boxes = _import.DebugBoxes();
                bool Overlap(Rect a, Rect b) => a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f;
                bool Inside(Rect inner, Rect outer) => inner.xMin >= outer.xMin - 0.5f && inner.yMin >= outer.yMin - 0.5f && inner.xMax <= outer.xMax + 0.5f && inner.yMax <= outer.yMax + 0.5f;
                var outside = boxes.Where(b => !Inside(b.rect, new Rect(0, 0, 1920, 1080))).Select(b => b.what).ToList();
                var overlaps = new List<string>();
                var strays = new List<string>();
                foreach (var group in boxes.Select(b => b.group).Distinct())
                {
                    var members = boxes.Where(b => b.group == group && !(group == "modal" && b.what == "panel")).ToList();
                    for (int i = 0; i < members.Count; i++)
                        for (int j = i + 1; j < members.Count; j++)
                            if (Overlap(members[i].rect, members[j].rect)) overlaps.Add($"{group}: {members[i].what} / {members[j].what}");
                    if (group == "page") continue;
                    var parent = group == "modal" ? boxes.FirstOrDefault(b => b.group == "modal" && b.what == "panel")
                        : boxes.FirstOrDefault(b => b.group == "page" && b.what == group);
                    if (parent.what == null) { strays.Add($"{group}: no parent"); continue; }
                    strays.AddRange(members.Where(m => !Inside(m.rect, parent.rect)).Select(m => $"{group}: {m.what}"));
                }
                Report($"{what}: {boxes.Count} boxes inside 1920x1080{(outside.Count > 0 ? ": not " + string.Join(", ", outside) : "")}, none overlapping"
                       + $"{(overlaps.Count > 0 ? ": " + string.Join("; ", overlaps.Take(4)) : "")}, each inside its panel{(strays.Count > 0 ? ": not " + string.Join(", ", strays) : "")}",
                    boxes.Count >= 9 && outside.Count == 0 && overlaps.Count == 0 && strays.Count == 0);
                var previews = boxes.Where(b => b.what.Contains("preview")).ToList();
                if (previews.Count > 0)
                    Report($"{what}: card back previews are card-shaped ({string.Join(", ", previews.Select(p => $"{p.what} {p.rect.width / p.rect.height:0.000}"))})",
                        previews.All(p => Mathf.Abs(p.rect.width / p.rect.height - CardArt.CardAspect) <= 0.01f));
                // One-line texts (boxes too short for two lines) must stay on one line; every text must fit.
                var cut = new List<string>();
                foreach (var t in _import.DebugTexts().Where(t => t.text.Length > 0))
                {
                    bool oneLine = t.rectTransform.rect.height < 2 * 1.15f * t.fontSize;
                    if (!TextFits(t) || (oneLine && t.cachedTextGenerator.lineCount != 1))
                        cut.Add($"\"{(t.text.Length > 40 ? t.text.Substring(0, 40) + "..." : t.text).Replace("\n", " ")}\" ({t.cachedTextGenerator.lineCount} lines)");
                }
                Report($"{what}: every text fits, one-line ones on one line{(cut.Count > 0 ? ": " + string.Join(", ", cut.Take(5)) : "")}", cut.Count == 0);
            }

            // ---- The menu: nine options, Import Cards between Settings and About ----
            Report("the game opens on the title screen", _title.IsOpen);
            _title.DebugPress();
            yield return new WaitForSecondsRealtime(0.6f);
            var order = new[]
            {
                MainMenu.Choice.Campaign, MainMenu.Choice.FreeDuel, MainMenu.Choice.TagDuel, MainMenu.Choice.DeckEditor, MainMenu.Choice.Replays,
                MainMenu.Choice.Settings, MainMenu.Choice.ImportCards, MainMenu.Choice.About, MainMenu.Choice.Quit,
            };
            var items = _menu.DebugItems;
            Report($"the menu: {string.Join(", ", items.Select(i => i.title))}",
                _menu.IsOpen && items.Select(i => i.choice).SequenceEqual(order) && items[6].title == "IMPORT CARDS" && !items[6].locked);
            Canvas.ForceUpdateCanvases();
            var (geometry, noticeTop) = _menu.DebugGeometry();
            var geoBad = new List<string>();
            for (int i = 0; i < geometry.Count; i++)
            {
                var g = geometry[i];
                if (i == 0 && g.box.yMin < 200 - 0.5f) geoBad.Add($"{g.choice} starts at {g.box.yMin:0} (logo)");
                if (i > 0 && g.box.yMin < geometry[i - 1].box.yMax + 12 - 0.5f) geoBad.Add($"{g.choice} {g.box.yMin:0} < {geometry[i - 1].box.yMax:0}+12");
                bool Within(Rect r) => r.xMin >= g.box.xMin - 0.5f && r.yMin >= g.box.yMin - 0.5f && r.xMax <= g.box.xMax + 0.5f && r.yMax <= g.box.yMax + 0.5f;
                if (!Within(g.title) || !Within(g.subtitle)) geoBad.Add($"{g.choice}'s texts outside it");
                if (!TextFits(g.titleText) || g.titleText.cachedTextGenerator.lineCount != 1) geoBad.Add($"{g.choice}'s title doesn't fit");
                if (g.subtitleText.text.Length > 0 && (!TextFits(g.subtitleText) || (i > 0 && g.subtitleText.cachedTextGenerator.lineCount != 1)))
                    geoBad.Add($"{g.choice}'s subtitle doesn't fit ({g.subtitleText.cachedTextGenerator.lineCount} lines)");
            }
            var last = geometry[geometry.Count - 1].box;
            if (last.yMax > 1010.5f) geoBad.Add($"the last ends at {last.yMax:0} (> 1010)");
            if (noticeTop < last.yMax) geoBad.Add($"the notice at {noticeTop:0} is over it");
            Report($"the menu's nine options: {string.Join(", ", geometry.Select(g => $"{g.box.yMin:0}-{g.box.yMax:0}"))}, notice at {noticeTop:0}"
                   + $"{(geoBad.Count > 0 ? ": " + string.Join("; ", geoBad) : "")}", geoBad.Count == 0 && geometry.Count == 9);
            Report($"Import Cards' subtitle (\"{geometry[6].subtitleText.text}\")", geometry[6].subtitleText.text == "Add your own card pictures: a zip or image files.");
            yield return Shot("menu");

            var midDuel = _menu.MidDuel;
            _menu.MidDuel = () => "Your duel is waiting.";
            _menu.Open();
            // (Concede is shut too here: there's no duel behind this test menu to concede.)
            var shut = _menu.DebugItems.Where(i => i.locked && i.choice != MainMenu.Choice.Concede).Select(i => i.choice).ToList();
            _menu.DebugChoose(MainMenu.Choice.ImportCards);
            yield return null;
            Report($"over a campaign duel Import Cards is shut with Tag Duel, the Deck Editor and Replays ({string.Join(", ", shut)}; "
                   + $"\"{_menu.DebugItems.First(i => i.choice == MainMenu.Choice.ImportCards).title}\")",
                shut.Contains(MainMenu.Choice.ImportCards) && shut.Count == 4 && !_import.IsOpen && _menu.IsOpen);
            _menu.MidDuel = midDuel;
            _menu.Open();
            yield return Frames(2);

            // ---- Down to Import Cards and Enter ----
            for (int i = 0; i < 12 && _menu.DebugFocused != MainMenu.Choice.ImportCards; i++) yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
            yield return new WaitForSecondsRealtime(0.5f);
            Report("Down to Import Cards and Enter open the screen (the menu closes)", _import.IsOpen && !_menu.IsOpen && Idle());
            Report($"the highlight starts on Card Images' Import... ({_import.DebugFocusSection} {_import.DebugFocusLabel}); no accent button on the page ({string.Join(", ", _import.DebugPrimary)})",
                _import.DebugFocusLabel == "Import..." && _import.DebugFocusSection == "Card Images" && _import.DebugPrimary.Count == 0);
            var shown = Texts();
            var wanted = new[]
            {
                "IMPORT CARDS", ImportScreen.Privacy, ImportScreen.Hint, "Card Images", "High Resolution Card Images", "Card Back",
                "Pictures for the cards in duels, packs and the deck editor.", "Bigger pictures, used when a card is shown large. Optional.",
                "The picture on the back of every card.", "No pictures yet", "The game's card back", "Import...", "Open folder", "Back",
            };
            var missing = wanted.Where(w => !shown.Contains(w)).ToList();
            Report($"every line shows{(missing.Count > 0 ? ": missing " + string.Join(" | ", missing) : "")}", missing.Count == 0);
            string Full(string p) => Path.GetFullPath(p);
            Report($"the path lines are the folders ({_import.DebugRow(PictureKind.Standard).path} | {_import.DebugRow(PictureKind.Hd).path} | {_import.DebugRow(PictureKind.Back).path})",
                _import.DebugRow(PictureKind.Standard).path == Full(art.ImagesFolder) && _import.DebugRow(PictureKind.Hd).path == Full(art.HdFolder)
                && _import.DebugRow(PictureKind.Back).path == Full(art.CardsFolder));
            Report($"the HD folder is read, once ({art.Folders.Count(f => string.Equals(Full(f).TrimEnd('\\', '/'), Full(art.HdFolder), StringComparison.OrdinalIgnoreCase))} times in {string.Join(" + ", art.Folders)})",
                art.Folders.Count(f => string.Equals(Full(f).TrimEnd('\\', '/'), Full(art.HdFolder), StringComparison.OrdinalIgnoreCase)) == 1);
            Layout("the idle screen");
            yield return Shot("idle");

            // A 2000-character path still takes one line, cut in the middle.
            var sep = Path.DirectorySeparatorChar.ToString();
            var deep = Path.GetPathRoot(Full(folder)) + string.Join(sep, Enumerable.Repeat("a-very-long-folder-name", 85)) + sep + string.Join(sep, "mods", "cards", "images");
            var line = _import.DebugPathLine(PictureKind.Standard, deep);
            Canvas.ForceUpdateCanvases();
            bool deepOk = line.text.Contains("…") && line.text.EndsWith(string.Join(sep, "mods", "cards", "images")) && line.cachedTextGenerator.lineCount == 1 && TextFits(line);
            var deepText = line.text;
            var wide = Path.GetPathRoot(Full(folder)) + new string('x', 1000) + sep + new string('y', 1000);
            line = _import.DebugPathLine(PictureKind.Standard, wide);
            Canvas.ForceUpdateCanvases();
            bool wideOk = line.text.Contains("…") && line.text.EndsWith("yyy") && line.cachedTextGenerator.lineCount == 1 && TextFits(line);
            Report($"a {deep.Length}-character path shows on one line (\"{deepText}\"), and a {wide.Length}-character one of two folders ({line.text.Length} characters)", deepOk && wideOk);
            _import.DebugRefreshRows();

            // ---- The real Import... button: the dialog refuses in a check run, and says so ----
            yield return Press(Key.Enter);
            yield return Frames(2);
            Report($"Enter on Import... asks for the file window, which a check run refuses: \"{_import.DebugStatus}\" (red: {_import.DebugStatusColor == UiTheme.Current.Bad})",
                _import.DebugStatus == "The file window can't open: automated checks don't open the file dialog." && _import.DebugStatusColor == UiTheme.Current.Bad && Idle());

            // ---- Open folder: recorded, never opened ----
            _import.DebugOpened.Clear();
            yield return Press(Key.RightArrow);
            var onOpen = _import.DebugFocusLabel;
            yield return Press(Key.Enter);
            var folderUrl = new Uri(Full(art.ImagesFolder).TrimEnd('/', '\\') + Path.DirectorySeparatorChar).AbsoluteUri;
            Report($"Right, Enter: Open folder ({onOpen}) asks for {string.Join(", ", _import.DebugOpened)} and opens nothing (\"{_import.DebugStatus}\")",
                onOpen == "Open folder" && _import.DebugOpened.SequenceEqual(new[] { folderUrl })
                && _import.DebugStatus == $"Opened {Full(art.ImagesFolder)} (not opened: automated check)");

            // ---- The keys: a grid of rows and columns, then Back ----
            var path = new List<string>();
            string Here() => $"{(_import.DebugFocusSection.Length > 0 ? _import.DebugFocusSection + " " : "")}{_import.DebugFocusLabel}";
            foreach (var key in new[] { Key.DownArrow, Key.DownArrow, Key.DownArrow, Key.DownArrow, Key.UpArrow, Key.LeftArrow, Key.LeftArrow, Key.UpArrow, Key.UpArrow, Key.UpArrow })
            {
                yield return Press(key);
                path.Add(Here());
            }
            var expected = new[]
            {
                "High Resolution Card Images Open folder", "Card Back Open folder", "Back", "Back", "Card Back Import...", "Card Back Import...",
                "Card Back Import...", "High Resolution Card Images Import...", "Card Images Import...", "Card Images Import...",
            };
            Report($"the arrows: {string.Join(" > ", path)} (gold frame {_import.DebugFrameShown})", path.SequenceEqual(expected) && _import.DebugFrameShown);

            // ---- A zip into Card Images ----
            int before = art.CountIn(art.ImagesFolder);
            _import.DebugImport(PictureKind.Standard, new[] { redZip }, null);
            var reading = _import.DebugActivityLine;
            yield return Until(Idle, 30);
            Report($"a zip: \"{reading}\", then \"{_import.DebugStatus}\" ({before} -> {art.CountIn(art.ImagesFolder)} pictures, \"{_import.DebugRow(PictureKind.Standard).count}\")",
                reading == "Reading red-cards.zip..." && _import.DebugStatus == "Card Images: 4 added, 1 skipped." && _import.DebugStatusColor == UiTheme.Current.Good
                && art.CountIn(art.ImagesFolder) == before + 4 && _import.DebugRow(PictureKind.Standard).count == "4 pictures");
            Report($"its cards have their pictures now, red ({string.Join("; ", codes.Take(4).Select(c => Rgb(art.Get(c))))})",
                codes.Take(4).All(c => Near(art.Get(c), red)));
            var firstFile = Path.Combine(art.ImagesFolder, $"{codes[0]}.png");
            var cacheFile = ArtLoader.CacheFileOf(firstFile);
            Report($"the red picture is loaded and in the disk cache under its path ({cacheFile}), so a stale picture could show after a replace",
                cacheFile != null && File.Exists(cacheFile) && cacheFile.StartsWith(artCache));
            Report($"the highlight is back on Card Images' Import... ({Here()})", Here() == "Card Images Import...");
            yield return Shot("zip");

            // ---- A loose picture with a badly named file ----
            _import.DebugImport(PictureKind.Standard, new[] { greenLoose, cover }, null);
            var readingTwo = _import.DebugActivityLine;
            yield return Until(Idle, 30);
            Report($"a loose picture and cover.png: \"{readingTwo}\", then \"{_import.DebugStatus}\" (green: {Rgb(art.Get(codes[4]))})",
                readingTwo == "Reading 2 files..." && _import.DebugStatus == "Card Images: 1 added, 1 skipped." && Near(art.Get(codes[4]), green));

            // ---- A badly named file alone: the problem ----
            _import.DebugImport(PictureKind.Standard, new[] { cover }, null);
            yield return Until(() => _import.DebugState == ImportScreen.State.Problem, 30);
            yield return Frames(2);
            shown = Texts();
            Report($"cover.png alone can't be imported: \"That didn't work\", the reason, OK highlighted ({_import.DebugFocusLabel}); the page's buttons off",
                _import.DebugState == ImportScreen.State.Problem && shown.Contains("That didn't work")
                && shown.Contains("There are no card pictures in cover.png. Card pictures are named by card number, like 89631139.jpg.")
                && _import.DebugFocusLabel == "OK" && !_import.DebugPageButtonsOn);
            Layout("the problem");
            yield return FrameCheck("the problem", Key.LeftArrow);
            yield return Shot("problem");
            yield return Press(Key.Enter);
            Report("Enter on OK closes it; nothing was written", Idle() && art.CountIn(art.ImagesFolder) == before + 5 && _import.DebugPageButtonsOn);

            // ---- The same zip again: Keep mine (the default) ----
            _import.DebugImport(PictureKind.Standard, new[] { redZip }, null);
            yield return Until(() => _import.DebugState == ImportScreen.State.Question, 30);
            yield return Frames(2);
            shown = Texts();
            Report($"the same zip again asks once: \"All 4 of these pictures already exist.\", Keep mine highlighted ({_import.DebugFocusLabel}) and the only accent ({string.Join(", ", _import.DebugPrimary)})"
                   ,
                shown.Contains("All 4 of these pictures already exist.") && shown.Contains("New: 0\nAlready there: 4\nSkipped: 1")
                && System.Text.RegularExpressions.Regex.IsMatch(shown, @"Skipped because: (pics/)?notes\.txt: not a card number") && shown.Contains("Replace them with the new ones, or keep yours?")
                && _import.DebugFocusLabel == "Keep mine" && _import.DebugPrimary.SequenceEqual(new[] { "Keep mine" }) && !_import.DebugPageButtonsOn);
            Layout("the overwrite question");
            yield return FrameCheck("the overwrite question", Key.RightArrow, Key.LeftArrow);
            yield return Shot("question");
            yield return Press(Key.Enter);
            yield return Until(Idle, 30);
            Report($"Enter keeps them: \"{_import.DebugStatus}\", still red ({Rgb(art.Get(codes[0]))})",
                _import.DebugStatus == "Card Images: nothing new. 4 kept, 1 skipped." && _import.DebugStatusColor == UiTheme.Current.TextSoft && Near(art.Get(codes[0]), red));

            // ---- Cancel: on the question (Esc), and while reading ----
            _import.DebugImport(PictureKind.Standard, new[] { blueZip }, null);
            yield return Until(() => _import.DebugState == ImportScreen.State.Question, 30);
            yield return Frames(2);
            yield return Press(Key.Escape);
            Report($"Esc on the question cancels it: \"{_import.DebugStatus}\", still red ({Rgb(art.Get(codes[0]))})",
                Idle() && _import.DebugStatus == "Import cancelled. Nothing was changed." && Near(art.Get(codes[0]), red));
            _import.DebugImport(PictureKind.Standard, new[] { blueZip }, null);
            var readingState = _import.DebugState;
            Layout("reading");
            _import.Back(); // Esc / B while reading
            yield return Frames(30);
            Report($"Cancel while reading ({readingState}): \"{_import.DebugStatus}\", the scan's result ignored ({_import.DebugState})",
                readingState == ImportScreen.State.Scanning && Idle() && _import.DebugStatus == "Import cancelled. Nothing was changed." && Near(art.Get(codes[0]), red));

            // ---- A blue zip: Replace them; the cards show the new picture (loaded pictures forgotten by Refresh; the disk
            // cache entry, same path, is rejected by its size and write time check and made again) ----
            _import.DebugImport(PictureKind.Standard, new[] { blueZip }, null);
            yield return Until(() => _import.DebugState == ImportScreen.State.Question, 30);
            yield return Frames(2);
            yield return Press(Key.LeftArrow);
            var onReplace = _import.DebugFocusLabel;
            yield return Press(Key.Enter);
            yield return Until(Idle, 30);
            Report($"Left, Enter: Replace them ({onReplace}): \"{_import.DebugStatus}\"",
                onReplace == "Replace them" && _import.DebugStatus == "Card Images: 4 replaced, 1 skipped." && _import.DebugStatusColor == UiTheme.Current.Good);
            Report($"{codes[3]}.jpg became {codes[3]}.png (a card never has two pictures)",
                !File.Exists(Path.Combine(art.ImagesFolder, $"{codes[3]}.jpg")) && File.Exists(Path.Combine(art.ImagesFolder, $"{codes[3]}.png")));
            Report($"the replaced cards show the new picture, blue, not the red one loaded and disk-cached under the same path "
                   + $"({string.Join("; ", codes.Take(4).Select(c => Rgb(art.Get(c))))}; whole: {Rgb(art.GetFull(codes[0]))})",
                codes.Take(4).All(c => Near(art.Get(c), blue)) && Near(art.GetFull(codes[0]), blue));
            yield return Shot("replaced");

            // ---- HD: its own folder, read once even when settings.txt names it too ----
            int hdBefore = art.CountIn(art.HdFolder);
            _import.DebugImport(PictureKind.Hd, new[] { hdLoose }, null);
            yield return Until(Idle, 30);
            Report($"HD: \"{_import.DebugStatus}\" ({hdBefore} -> {art.CountIn(art.HdFolder)}; \"{_import.DebugRow(PictureKind.Hd).count}\"), the card shows it ({Rgb(art.Get(codes[5]))})",
                _import.DebugStatus == "High Resolution Card Images: 1 added." && art.CountIn(art.HdFolder) == hdBefore + 1
                && _import.DebugRow(PictureKind.Hd).count == "1 picture" && Near(art.Get(codes[5]), purple));
            var settingsFile = Path.Combine(art.ModsRoot, "settings.txt");
            var settingsBefore = File.Exists(settingsFile) ? File.ReadAllText(settingsFile) : null;
            File.AppendAllText(settingsFile, $"\nhd_pics: {art.HdFolder}{Path.DirectorySeparatorChar}\n");
            var reread = new CardArt(_cardPack, art.ModsRoot, art.EdoproFolder);
            ArtLoader.CacheFolder = artCache; // a new CardArt sets the game's
            int hdTimes = reread.Folders.Count(f => string.Equals(Full(f).TrimEnd('\\', '/'), Full(art.HdFolder), StringComparison.OrdinalIgnoreCase));
            Report($"with \"hd_pics: {art.HdFolder}{Path.DirectorySeparatorChar}\" in settings.txt the HD folder is read once ({hdTimes}; HD folders {string.Join(" + ", reread.HdFolders)})",
                hdTimes == 1 && reread.HdFolders.Count == 1);
            if (settingsBefore == null) File.Delete(settingsFile);
            else File.WriteAllText(settingsFile, settingsBefore);

            // ---- A big import: 6,000 pictures, then how long the refresh after it takes on the main thread ----
            var bigStarted = Time.realtimeSinceStartup;
            _import.DebugImport(PictureKind.Hd, new[] { bigZip }, null);
            yield return Until(() => (_import.DebugState == ImportScreen.State.Importing && _import.DebugProgress.done > 0) || Idle(), 60);
            var importingLine = _import.DebugActivityLine;
            Layout("importing");
            yield return Shot("importing");
            yield return Until(Idle, 300);
            var refreshMs = _import.DebugRefreshMs;
            Report($"6,000 pictures into HD: \"{_import.DebugStatus}\" in {Time.realtimeSinceStartup - bigStarted:0.0} s; the refresh after it took {refreshMs:0.00} ms (at most 500)",
                importingLine == "Importing into High Resolution Card Images..." && _import.DebugStatus == "High Resolution Card Images: 6,000 added."
                && art.CountIn(art.HdFolder) == hdBefore + 6001 && refreshMs >= 0 && refreshMs <= 500);

            // ---- Cancel while importing (the same 6,000 again, Replace them): stops between pictures, nothing half-saved ----
            _import.DebugImport(PictureKind.Hd, new[] { bigZip }, true);
            yield return Until(() => (_import.DebugState == ImportScreen.State.Importing && _import.DebugProgress.done > 0) || Idle(), 60);
            var progress = _import.DebugProgress;
            _import.Back(); // Esc / B while importing: Cancel
            var stopping = _import.DebugActivityLine;
            yield return Until(Idle, 120);
            var stale = Directory.GetFiles(art.HdFolder, "*.importing");
            Report($"Cancel while importing ({progress.done} of {progress.total}): \"{stopping}\", then \"{_import.DebugStatus}\" ({stale.Length} .importing files, {art.CountIn(art.HdFolder)} pictures)",
                stopping == "Stopping..." && progress.done < progress.total
                && _import.DebugStatus.StartsWith("Import stopped: ") && _import.DebugStatus.EndsWith(" saved before it stopped. Nothing half-saved was left.")
                && art.CountIn(art.HdFolder) == hdBefore + 6001 && stale.Length == 0);

            // ---- The card back: always asked, the old and the new side by side ----
            var backBefore = art.Back;
            _import.DebugImport(PictureKind.Back, new[] { yellowBack }, null);
            yield return Until(() => _import.DebugState == ImportScreen.State.BackConfirm, 30);
            yield return Frames(2);
            shown = Texts();
            Report($"a card back asks first: \"Use this card back?\", Use this back highlighted ({_import.DebugFocusLabel}) and the only accent ({string.Join(", ", _import.DebugPrimary)})",
                shown.Contains("Use this card back?") && shown.Contains("The game's card back stays in the game; this one is used instead.")
                && shown.Contains("Now") && shown.Contains("New") && _import.DebugFocusLabel == "Use this back" && _import.DebugPrimary.SequenceEqual(new[] { "Use this back" }));
            Layout("the card back question");
            yield return FrameCheck("the card back question", Key.LeftArrow);
            yield return Shot("back-question");
            yield return Press(Key.Enter);
            yield return Until(Idle, 30);
            var backPng = Path.Combine(art.CardsFolder, "back.png");
            var backJpg = Path.Combine(art.CardsFolder, "back.jpg");
            Report($"Enter uses it: \"{_import.DebugStatus}\"; back.png there; the card back is yellow ({Rgb(art.Back)}), the row says \"{_import.DebugRow(PictureKind.Back).count}\" and shows it",
                _import.DebugStatus == "Card back changed." && _import.DebugStatusColor == UiTheme.Current.Good && File.Exists(backPng)
                && art.Back != null && art.Back != backBefore && Near(art.Back, yellow)
                && _import.DebugRow(PictureKind.Back).count == "Your own card back" && _import.DebugRowPreview == art.Back
                && _import.DebugRow(PictureKind.Back).path == Full(backPng));
            yield return Shot("back");
            var yellowTexture = art.Back;
            _import.DebugImport(PictureKind.Back, new[] { cyanBack }, true);
            yield return Until(Idle, 30);
            Report($"a .jpg back over it: \"{_import.DebugStatus}\"; back.jpg there, back.png gone; cyan ({Rgb(art.Back)})",
                _import.DebugStatus == "Card back changed." && File.Exists(backJpg) && !File.Exists(backPng) && art.Back != yellowTexture && Near(art.Back, cyan));

            // ---- FilePicker's pure helpers (the Windows dialog's buffer and filter; the dialog itself never opens here) ----
            bool Same(string[] got, params string[] want) => got.SequenceEqual(want);
            var parsed = new (string what, bool ok)[]
            {
                ("one file", Same(FilePicker.ParseMultiSelect("C:\\pics\\89631139.jpg"), "C:\\pics\\89631139.jpg")),
                ("several", Same(FilePicker.ParseMultiSelect("C:\\pics\0a.png\0b.zip"), "C:\\pics\\a.png", "C:\\pics\\b.zip")),
                ("a drive root", Same(FilePicker.ParseMultiSelect("C:\\\0a.png\0b.png"), "C:\\a.png", "C:\\b.png")),
                ("empty", Same(FilePicker.ParseMultiSelect("")) && Same(FilePicker.ParseMultiSelect(new char[] { '\0', '\0' }))),
                ("a whole buffer", Same(FilePicker.ParseMultiSelect("C:\\pics\0a.png\0b.png\0\0left over".ToCharArray()), "C:\\pics\\a.png", "C:\\pics\\b.png")),
                ("a filter", FilePicker.FilterString("Card pictures and zips", new[] { "zip", "jpg", ".png" }) == "Card pictures and zips (*.zip;*.jpg;*.png)\0*.zip;*.jpg;*.png\0\0"),
                ("no extensions", FilePicker.FilterString("Files", Array.Empty<string>()) == "Files (*.*)\0*.*\0\0"),
            };
            Report($"FilePicker's buffer and filter helpers: {string.Join(", ", parsed.Select(p => $"{p.what} {(p.ok ? "ok" : "WRONG")}"))}", parsed.All(p => p.ok));

            // ---- -hdpics off: the HD folder isn't read this run, so the screen says so and won't import into it ----
            CardView.Art = new CardArt(_cardPack, art.ModsRoot, art.EdoproFolder, new[] { "off" });
            ArtLoader.CacheFolder = artCache;
            _import.Close();
            _import.Open();
            yield return Frames(2);
            var offRow = _import.DebugRow(PictureKind.Hd);
            bool offRefused = !_import.DebugImport(PictureKind.Hd, new[] { hdLoose }, null);
            Report($"with -hdpics off the HD row says \"{offRow.count}\", its Import... is off ({!_import.DebugImportOn(PictureKind.Hd)}) and refuses ({offRefused}); Card Images' still on",
                offRow.count == "Not used on this run (-hdpics off)" && !_import.DebugImportOn(PictureKind.Hd) && offRefused && Idle()
                && _import.DebugImportOn(PictureKind.Standard));
            Layout("-hdpics off");
            CardView.Art = art;
            _import.Close();
            _import.Open();
            yield return Frames(2);
            Report($"back to the usual run: \"{_import.DebugRow(PictureKind.Hd).count}\", Import... on", _import.DebugImportOn(PictureKind.Hd) && _import.DebugRow(PictureKind.Hd).count == "6,001 pictures");

            // ---- Back to the menu: Esc, then B (the controller's back) ----
            yield return Press(Key.Escape);
            yield return new WaitForSecondsRealtime(0.3f);
            Report("Esc on the screen goes back to the menu", !_import.IsOpen && _menu.IsOpen);
            _menu.DebugChoose(MainMenu.Choice.ImportCards);
            yield return new WaitForSecondsRealtime(0.3f);
            bool reopened = _import.IsOpen;
            ControllerBack();
            yield return new WaitForSecondsRealtime(0.3f);
            Report("the controller's B goes back too", reopened && !_import.IsOpen && _menu.IsOpen);
            InputSystem.RemoveDevice(keyboard);

            Debug.Log($"import check: done, {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
#endif
        }
    }
}
