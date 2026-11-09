using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-aboutcheck &lt;folder&gt; [-aboutlicenses &lt;folder&gt;]` (absolute; with -mods &lt;empty folder&gt;): the About screen.
    /// <list type="number">
    /// <item>The main menu offers About; Down and Enter open it.</item>
    /// <item>Every notice shows: name, version, copyright, the license notice (AGPL, no warranty, CC BY-SA, the name and
    /// logo), the disclaimer and the credits; nothing overlaps, everything is inside 1920x1080, every text fits its box.</item>
    /// <item>View license shows the AGPL's text (from -aboutlicenses, the build's StreamingAssets/licenses, or the repo in
    /// the editor; else a test folder written here) and scrolls; Third-party notices shows the notices as plain text.</item>
    /// <item>Open licenses folder and Source code open nothing in a check run (what they'd open is recorded).</item>
    /// <item>A missing file reads as a line saying where it is online.</item>
    /// <item>The keyboard and a controller reach every button; Esc and B close the reader, then go back to the menu.</item>
    /// </list>
    /// </summary>
    public sealed partial class DuelController
    {
        private const string AgplFirstLine = "GNU AFFERO GENERAL PUBLIC LICENSE";

        private IEnumerator AboutCheck(string folder, string? licenses)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"about check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"about-{name}.png"));
                yield return null;
                yield return null;
            }
            IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
            yield return new WaitForSecondsRealtime(1.5f);

            // ---- Where the files come from ----
            if (licenses != null) LicenseFiles.FolderOverride = licenses;
            var gameFolder = LicenseFiles.Folder;
            bool haveReal = LicenseFiles.Find(LicenseFiles.Doc.License) != null;
            // A test folder of our own: a license that starts as the AGPL does, and notices with every Markdown mark the
            // reader has to make plain.
            var testFolder = Path.Combine(folder, "licenses-test");
            Directory.CreateDirectory(testFolder);
            File.WriteAllText(Path.Combine(testFolder, "LICENSE.txt"),
                "                    " + AgplFirstLine + "\n                       Version 3, 19 November 2007\n\n (A test copy written by the About check.)\n");
            File.WriteAllText(Path.Combine(testFolder, "UNITY-PERMISSION.md"), "# Additional permission: the Unity runtime\n\n> **Additional permission under GNU AGPL version 3 section 7**\n");
            File.WriteAllText(Path.Combine(testFolder, "THIRD_PARTY_NOTICES.md"),
                "# Third-party notices\n\nSee [LICENSING.md](LICENSING.md) and [the repo](https://github.com/fullstackjake/duel-circuit).\n\n"
                + "| Component | License |\n|---|---|\n| [ygopro-core](#ygopro-core) | **AGPL-3.0-or-later** |\n\n## ygopro-core\n\n- **Pinned commit:** `baa3881`\n\n"
                + "```\nTHE SOFTWARE IS PROVIDED \"AS IS\"\n```\n");
            File.WriteAllText(Path.Combine(testFolder, "SOURCE.txt"), "Duel Circuit 0.0.0\nBuilt from commit 0123456789abcdef0123456789abcdef01234567\n");
            if (!haveReal) LicenseFiles.FolderOverride = testFolder;
            Report($"the license files are read from {LicenseFiles.Folder} ({(haveReal ? "the game's own" : $"the game's own folder {gameFolder} has none: the test folder")})",
                LicenseFiles.Find(LicenseFiles.Doc.License) != null);

#if ENABLE_INPUT_SYSTEM
            var keyboard = InputSystem.AddDevice<Keyboard>("AboutCheckKeys");
            keyboard.MakeCurrent(); // the menu reads Keyboard.current
            IEnumerator Press(Key key)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
                yield return Frames(3);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return Frames(3);
            }
#endif

            // ---- From the title to the menu to About, as a player would ----
            Report("the game opens on the title screen", _title.IsOpen);
            _title.DebugPress();
            yield return new WaitForSecondsRealtime(0.6f);
            var items = _menu.DebugItems;
            Report($"the main menu offers About ({string.Join(", ", items.Select(i => i.title))})",
                _menu.IsOpen && items.Any(i => i.choice == MainMenu.Choice.About && i.title == "ABOUT" && !i.locked)
                && items.FindIndex(i => i.choice == MainMenu.Choice.About) == items.FindIndex(i => i.choice == MainMenu.Choice.Quit) - 1);
            yield return Shot("menu");
#if ENABLE_INPUT_SYSTEM
            for (int i = 0; i < 10 && _menu.DebugFocused != MainMenu.Choice.About; i++) yield return Press(Key.DownArrow);
            yield return Press(Key.Enter);
#else
            _menu.DebugChoose(MainMenu.Choice.About);
#endif
            yield return new WaitForSecondsRealtime(0.5f);
            Report("Down to About and Enter open the About screen (the menu closes)", _about.IsOpen && !_menu.IsOpen && !_about.ReaderOpen);
            yield return Shot("main");

            // ---- Every notice shows ----
            var shown = string.Join("\n", _about.DebugTexts(false).Select(t => t.text));
            var wanted = new[]
            {
                "Duel Circuit", GameInfo.Version, "Copyright (C) 2026 Jake Joseph and the Duel Circuit authors.",
                "free software", "GNU Affero General Public License", "version 3 or (at your option) any later version", "build it with Unity",
                "NO WARRANTY", "CC BY-SA 4.0", "The name \"Duel Circuit\" and the logo aren't covered",
                "unofficial fan game", "not made, approved or supported by Konami or Shueisha", "card names and card text belong to their owners",
                "No card pictures ship", "ygopro-core", "Rules engine (ygopro-core), card scripts, card database and ban lists: Project Ignis (EDOPro)",
                "Card-set data: YGOPRODeck", "Historic decks and ban lists: Format Library and yugiohmeta.com", "Made with Unity",
                "github.com/fullstackjake/duel-circuit",
            }.Concat(AboutScreen.ButtonLabels).ToList();
            var missing = wanted.Where(w => !shown.Contains(w)).ToList();
            Report($"every notice shows ({wanted.Count} texts){(missing.Count > 0 ? ": missing " + string.Join(" | ", missing) : "")}", missing.Count == 0);
            Report($"the version is the title screen's ({GameInfo.Version})", shown.Contains($"Duel Circuit   {GameInfo.Version}"));
            yield return AboutLayout(Report, false, "the About screen");

#if ENABLE_INPUT_SYSTEM
            // ---- The keyboard reaches every button ----
            for (int i = 0; i < 6; i++) yield return Press(Key.LeftArrow);
            var reached = new List<string> { _about.DebugFocusLabel };
            for (int i = 0; i < 6; i++) { yield return Press(Key.RightArrow); reached.Add(_about.DebugFocusLabel); }
            reached = reached.Distinct().ToList();
            Report($"Left/Right reach every button ({string.Join(", ", reached)}), the gold frame on it ({_about.DebugFrameShown})",
                reached.SequenceEqual(AboutScreen.ButtonLabels) && _about.DebugFrameShown);
            yield return Shot("keys");

            // ---- View license: the AGPL ----
            for (int i = 0; i < 6 && _about.DebugFocusLabel != "View license"; i++) yield return Press(Key.LeftArrow);
            yield return Press(Key.Enter);
            yield return new WaitForSecondsRealtime(0.4f);
            var license = _about.DebugReader;
            var firstLine = license.text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            Report($"Enter on View license opens the license (\"{license.title}\", {license.from}); its first line is \"{firstLine}\"",
                _about.ReaderOpen && license.title == "LICENSE" && firstLine == AgplFirstLine);
            Report("the Unity permission follows the AGPL in it", license.text.Contains("Additional permission under GNU AGPL version 3 section 7"));
            if (haveReal)
                Report($"it's the whole AGPL ({license.text.Length} characters, to \"END OF TERMS AND CONDITIONS\" and its last section)",
                    license.text.Contains("END OF TERMS AND CONDITIONS") && license.text.Contains("How to Apply These Terms to Your New Programs") && license.text.Length > 30000);
            yield return Shot("license");
            yield return AboutLayout(Report, true, "the license reader");
            var top = _about.DebugScroll;
            yield return Press(Key.DownArrow);
            yield return Press(Key.DownArrow);
            var down = _about.DebugScroll;
            yield return Press(Key.PageDown);
            var page = _about.DebugScroll;
            yield return Press(Key.End);
            var end = _about.DebugScroll;
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("license-end");
            yield return Press(Key.Home);
            var home = _about.DebugScroll;
            Report($"it scrolls: Down {top.at:0} -> {down.at:0}, Page Down -> {page.at:0}, End -> {end.at:0} of {end.room:0}, Home -> {home.at:0}",
                top.at == 0 && (end.room <= 0 || (down.at > 0 && page.at > down.at && Mathf.Abs(end.at - end.room) < 1 && home.at == 0)));
            yield return Press(Key.Escape);
            Report("Esc closes the reader and leaves About open", !_about.ReaderOpen && _about.IsOpen);
#endif

            // ---- Third-party notices: Markdown made plain ----
            _about.ShowNotices();
            yield return new WaitForSecondsRealtime(0.3f);
            var notices = _about.DebugReader;
            var marks = new[] { "**", "](", "```", "|---", "\n# ", "\n## " }.Where(m => ("\n" + notices.text).Contains(m)).ToList();
            Report($"Third-party notices open as plain text (\"{notices.title}\", {notices.text.Length} characters{(marks.Count > 0 ? ", Markdown left: " + string.Join(" ", marks) : "")})",
                _about.ReaderOpen && notices.title == "THIRD-PARTY NOTICES" && notices.text.StartsWith("THIRD-PARTY NOTICES") && marks.Count == 0 && notices.text.Contains("ygopro-core"));
            yield return Shot("notices");
            _about.Back();
            Report("Back closes the reader", !_about.ReaderOpen && _about.IsOpen);

            // ---- The test folder: what the Markdown reader makes of each mark ----
            LicenseFiles.FolderOverride = testFolder;
            _about.ShowNotices();
            var plain = _about.DebugReader.text;
            Debug.Log($"about check: the test notices read:\n{plain}");
            Report("the test notices: headings without #, links as words (web ones with their address), table cells joined, bold and code marks gone, code kept",
                plain.StartsWith("THIRD-PARTY NOTICES\n") && plain.Contains("See LICENSING.md and the repo (https://github.com/fullstackjake/duel-circuit).")
                && plain.Contains("Component · License") && plain.Contains("ygopro-core · AGPL-3.0-or-later") && plain.Contains("\nYGOPRO-CORE\n")
                && plain.Contains("• Pinned commit: baa3881") && plain.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"") && !plain.Contains("|") && !plain.Contains("`"));
            _about.ShowLicense();
            Report("the test license's first line is the AGPL's", _about.DebugReader.text.TrimStart().StartsWith(AgplFirstLine));
            _about.Back();
            Report($"SOURCE.txt's commit shows cut to 7 characters ({LicenseFiles.BuiltFrom()})", LicenseFiles.BuiltFrom() == "Built from commit 0123456");

            // ---- Missing files: where they are online, no crash ----
            var empty = Path.Combine(folder, "licenses-empty");
            Directory.CreateDirectory(empty);
            LicenseFiles.FolderOverride = empty;
            _about.ShowLicense();
            yield return new WaitForSecondsRealtime(0.3f);
            var gone = _about.DebugReader;
            Report($"with no license file the reader says where it is online (\"{gone.text.Split('\n')[0]}\"; {_about.DebugStatus})",
                _about.ReaderOpen && gone.text.Contains("isn't in this copy of the game") && gone.text.Contains(LicenseFiles.OnlineUrl(LicenseFiles.Doc.License))
                && _about.DebugStatus.Contains(LicenseFiles.SourceUrl));
            yield return Shot("missing");
            _about.Back();
            _about.ShowNotices();
            Report("so do the notices", _about.DebugReader.text.Contains(LicenseFiles.OnlineUrl(LicenseFiles.Doc.ThirdParty)));
            _about.Back();
            LicenseFiles.FolderOverride = Path.Combine(folder, "no-such-folder");
            _about.DebugOpened.Clear();
            _about.OpenFolder();
            Report($"Open licenses folder with no folder opens nothing and says where they are ({_about.DebugStatus})",
                _about.DebugOpened.Count == 0 && _about.DebugStatus.Contains(LicenseFiles.SourceUrl));
            LicenseFiles.FolderOverride = haveReal ? licenses : testFolder;

#if ENABLE_INPUT_SYSTEM
            // ---- Open licenses folder and Source code: recorded, never opened in a check run ----
            _about.DebugOpened.Clear();
            for (int i = 0; i < 6 && _about.DebugFocusLabel != "Open licenses folder"; i++) yield return Press(Key.RightArrow);
            yield return Press(Key.Enter);
            yield return Press(Key.RightArrow);
            yield return Press(Key.Enter);
            var opened = _about.DebugOpened.ToList();
            var folderUrl = new System.Uri(Path.GetFullPath(LicenseFiles.Folder).TrimEnd('/', '\\') + Path.DirectorySeparatorChar).AbsoluteUri;
            Report($"Open licenses folder and Source code ask for {string.Join(" and ", opened)} and open neither in a check run ({_about.DebugStatus})",
                opened.SequenceEqual(new[] { folderUrl, LicenseFiles.SourceUrl }) && _about.DebugStatus.Contains("not opened") && _about.IsOpen && !_about.ReaderOpen);

            // ---- A controller: the d-pad along the buttons, A to open, B to close and go back ----
            var pad = InputSystem.AddDevice<Gamepad>("AboutCheckPad");
            pad.MakeCurrent();
            IEnumerator Pad(GamepadButton button)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
                yield return Frames(3);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return Frames(3);
            }
            for (int i = 0; i < 6; i++) yield return Pad(GamepadButton.DpadLeft);
            var padReached = new List<string> { _about.DebugFocusLabel };
            for (int i = 0; i < 6; i++) { yield return Pad(GamepadButton.DpadRight); padReached.Add(_about.DebugFocusLabel); }
            padReached = padReached.Distinct().ToList();
            Report($"the d-pad reaches every button ({string.Join(", ", padReached)}; highlight mode {ControllerSupport.HighlightMode})",
                padReached.SequenceEqual(AboutScreen.ButtonLabels) && ControllerSupport.HighlightMode);
            for (int i = 0; i < 6 && _about.DebugFocusLabel != "Third-party notices"; i++) yield return Pad(GamepadButton.DpadLeft);
            yield return Pad(GamepadButton.South);
            yield return new WaitForSecondsRealtime(0.3f);
            bool padOpened = _about.ReaderOpen && _about.DebugReader.title == "THIRD-PARTY NOTICES";
            var padTop = _about.DebugScroll;
            yield return Pad(GamepadButton.DpadDown);
            yield return Pad(GamepadButton.RightShoulder);
            var padDown = _about.DebugScroll;
            yield return Pad(GamepadButton.East);
            Report($"A on Third-party notices opens it ({padOpened}), the d-pad and RB scroll it ({padTop.at:0} -> {padDown.at:0} of {padDown.room:0}), B closes it ({!_about.ReaderOpen})",
                padOpened && (padDown.room <= 0 || padDown.at > padTop.at) && !_about.ReaderOpen && _about.IsOpen);
            yield return Pad(GamepadButton.East);
            yield return new WaitForSecondsRealtime(0.3f);
            Report("B on About goes back to the menu", !_about.IsOpen && _menu.IsOpen);
            InputSystem.RemoveDevice(pad);
            keyboard.MakeCurrent();

            // ---- Esc on About goes back to the menu, and the menu doesn't also read it ----
            _menu.DebugChoose(MainMenu.Choice.About);
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Press(Key.Escape);
            yield return new WaitForSecondsRealtime(0.3f);
            Report("Esc on About goes back to the menu", !_about.IsOpen && _menu.IsOpen);
            InputSystem.RemoveDevice(keyboard);
#endif
            // ---- The mouse: a click on Third-party notices, a wheel turn, a click on the reader's Back, a click on Back ----
            _menu.DebugChoose(MainMenu.Choice.About);
            yield return new WaitForSecondsRealtime(0.3f);
#if ENABLE_INPUT_SYSTEM
            var mouse = InputSystem.AddDevice<Mouse>("AboutCheckMouse");
            Vector2 Center(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            IEnumerator Click(Vector2 at)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at });
                yield return Frames(3);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left, true));
                yield return Frames(2);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at });
                yield return Frames(3);
            }
            yield return Click(Center(_about.DebugButton("Third-party notices")));
            yield return new WaitForSecondsRealtime(0.3f);
            bool clickedOpen = _about.ReaderOpen && _about.DebugReader.title == "THIRD-PARTY NOTICES";
            var wheelFrom = _about.DebugScroll;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width / 2f, Screen.height / 2f), scroll = new Vector2(0, -360) });
            yield return Frames(4);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = new Vector2(Screen.width / 2f, Screen.height / 2f) });
            yield return Frames(4);
            var wheelTo = _about.DebugScroll;
            yield return Click(Center(_about.DebugButton("", reader: true)));
            bool clickedClose = !_about.ReaderOpen && _about.IsOpen;
            yield return Click(Center(_about.DebugButton("Back")));
            yield return new WaitForSecondsRealtime(0.3f);
            Report($"with the mouse: a click opens Third-party notices ({clickedOpen}), the wheel scrolls it ({wheelFrom.at:0} -> {wheelTo.at:0}), "
                   + $"the reader's Back closes it ({clickedClose}) and Back returns to the menu ({!_about.IsOpen && _menu.IsOpen})",
                clickedOpen && (wheelTo.room <= 0 || wheelTo.at > wheelFrom.at) && clickedClose && !_about.IsOpen && _menu.IsOpen);
            InputSystem.RemoveDevice(mouse);
#else
            _about.Back();
#endif
            LicenseFiles.FolderOverride = null;

            Debug.Log($"about check: done, {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// <summary>The layout check's rules on About (or its reader): no two boxes overlap, all inside 1920x1080, every text fits.</summary>
        private IEnumerator AboutLayout(System.Action<string, bool> report, bool reader, string what)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            var boxes = _about.DebugBoxes(reader);
            var overlaps = new List<string>();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i].rect;
                    var b = boxes[j].rect;
                    if (a.xMin < b.xMax - 0.5f && b.xMin < a.xMax - 0.5f && a.yMin < b.yMax - 0.5f && b.yMin < a.yMax - 0.5f)
                        overlaps.Add($"{boxes[i].what} / {boxes[j].what}");
                }
            var outside = boxes.Where(b => b.rect.xMin < -0.5f || b.rect.yMin < -0.5f || b.rect.xMax > 1920.5f || b.rect.yMax > 1080.5f).Select(b => b.what).ToList();
            report($"{what}: {boxes.Count} boxes, none overlapping{(overlaps.Count > 0 ? ": " + string.Join("; ", overlaps.Take(4)) : "")}"
                   + $", all inside 1920x1080{(outside.Count > 0 ? ": " + string.Join(", ", outside) : "")}", boxes.Count >= 5 && overlaps.Count == 0 && outside.Count == 0);
            var cut = _about.DebugTexts(reader).Where(t => t.text.Length > 0 && !TextFits(t))
                .Select(t => $"\"{(t.text.Length > 40 ? t.text.Substring(0, 40) + "..." : t.text).Replace("\n", " ")}\"").Distinct().ToList();
            report($"{what}: every text fits its box{(cut.Count > 0 ? ": " + string.Join(", ", cut.Take(5)) : "")}", cut.Count == 0);
        }
    }
}
