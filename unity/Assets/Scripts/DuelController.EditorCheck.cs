#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-editorcheck &lt;folder&gt;` (absolute; with -mods &lt;empty folder&gt;): the deck editor's quick fixes
    /// (docs/dev/EDITOR-QUICK-FIXES-SPEC-2026-11-20.md, Part U): the leave question and the unsaved mark (A), which list a
    /// deck is checked against (B), the keys (D), the red badges and clickable problem lines (E), free play's ownership line
    /// (F). Through a test mouse, keyboard and gamepad the UI treats as real ones, as -focuscheck does. Each step fails on the
    /// editor before this build. Screenshots of the mark, the question, the badges and links, the amber "No limits" line.
    /// </summary>
    public sealed partial class DuelController
    {
        private IEnumerator EditorCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"editor check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"editor-{name}.png"));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mods") < 0)
            {
                Report("needs -mods <empty folder> (it saves decks and the editor writes its recovery copies there)", false);
                Application.Quit();
                yield break;
            }
            Debug.Log($"editor check: window {Screen.width}x{Screen.height}");

            var mouse = InputSystem.AddDevice<Mouse>("EditorCheckMouse");
            var keys = InputSystem.AddDevice<Keyboard>("EditorCheckKeys");
            var pad = InputSystem.AddDevice<Gamepad>("EditorCheckPad");
            void Put(Vector2 at, bool left = false) => InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left, left));
            IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
            IEnumerator Click(Vector2 at)
            {
                Put(at);
                yield return Frames(2);
                Put(at, true);
                yield return Frames(2);
                Put(at);
                yield return Frames(2);
            }
            IEnumerator Press(params Key[] held)
            {
                InputSystem.QueueStateEvent(keys, new KeyboardState(held));
                yield return Frames(2);
                InputSystem.QueueStateEvent(keys, new KeyboardState());
                yield return Frames(2);
            }
            IEnumerator PadPress(GamepadButton button)
            {
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
                yield return Frames(3);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return Frames(3);
            }
            IEnumerator Wheel(Vector2 at, float notches)
            {
                Put(at);
                yield return Frames(2);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at, scroll = new Vector2(0, notches) });
                yield return Frames(1);
                Put(at);
                yield return Frames(2);
            }
            var off = new Vector2(-50, -50);
            Vector2 R(int slot) => _editor.DebugResultOnScreen(slot) ?? off;
            IEnumerator ClickButton(string label)
            {
                var at = _editor.DebugButtonOnScreen(label);
                if (at == null) Report($"(the {label} button is there)", false);
                else yield return Click(at.Value);
            }
            string Deck() { var d = _editor.DebugDeck.deck; return $"{string.Join(",", d.Main)}|{string.Join(",", d.Extra)}|{string.Join(",", d.Side)}"; }
            string Focus() { var f = _editor.DebugFocus; return $"{f.zone} {f.card} (ring {f.ring}; left: {f.panel})"; }
            string Plain(string s) => Regex.Replace(s, "<.*?>", "");
            uint Code(string name) => _cardPack!.All.First(c => c.Name == name && c.Alias == 0).Code;
            var own = _library.OwnFolder;
            string DeckFile(string name) => Path.Combine(own, DeckLibrary.SafeName(name) + ".ydk");
            string Recovery() => Path.Combine(own, "recovery.txt");
            var T = UiTheme.Current;
            string red = "D9433F", amber = "E3A21A";
            const string pot = "Pot of Greed", sangan = "Sangan", mst = "Mystical Space Typhoon", raigeki = "Raigeki", reborn = "Monster Reborn";
            // Forty cards no list forbids or limits (Normal monsters): a deck that's legal under every TCG list that has them out.
            var forty = _cardPack!.All.Where(c => c.Alias == 0 && (c.Type & CardType.Monster) != 0 && (c.Type & 0x10) != 0 && (c.Type & 0x20) == 0
                                                  && (c.Type & 0x4000) == 0 && !c.IsExtraDeckMonster && c.Code < 100000000)
                .Take(14).SelectMany(c => Enumerable.Repeat(c.Code, 3)).Take(40).ToList();
            void Name(string name) => _editor.DebugTextFields().First(f => f.name == "deck name").field.text = name; // as typing does (a change)

            // ==== Free play: the first opening (no list picked by hand) =====================================================
            _editor.Open(null);
            yield return new WaitForSecondsRealtime(0.6f);
            var lists = _editor.DebugBanlists;
            int newest = Banlist.NewestTcg(lists);
            var newestName = newest >= 0 ? lists[newest].Name : "(none)";

            // ---- D: on open the first result has the focus, not the search box --------------------------------------------
            Report($"on open the search box doesn't have the keyboard and the first result is focused ({Focus()})",
                !_editor.DebugSearchHasKeyboard && _editor.DebugFocus.ring == "result 0");
            Report($"the help lines name the keys (\"{_editor.DebugHelp.Split('\n').LastOrDefault()}\")",
                Regex.IsMatch(_editor.DebugHelp, @"(Ctrl|Cmd)\+S save · (Ctrl|Cmd)\+Z undo · (Ctrl|Cmd)\+F or / search · Esc back"));

            // ---- B: the newest TCG list ---------------------------------------------------------------------------------
            Report($"free play opens a new deck on the newest TCG list ({_editor.BanlistName}; the newest is {newestName})",
                newest >= 0 && _editor.BanlistName == newestName && newestName.Contains("TCG"));
            // ---- F: no ownership line in free play ------------------------------------------------------------------------
            Report($"free play's card detail has no ownership line ({_editor.DebugFocus.panel}: {_editor.DebugOwnershipLine})", _editor.DebugFocus.panel.Length > 0 && !_editor.DebugOwnershipLine);
            _editor.DebugSetDeck(new[] { Code(pot), Code(pot), Code(pot) }, Array.Empty<uint>());
            yield return Frames(2);
            var problems = Plain(_editor.DebugProblemsText);
            Report($"3 {pot} read as a problem under it, not \"legal\" (\"{problems.Replace("\n", " / ")}\")",
                problems.Contains($"{pot}: 3 copies") && !problems.Contains("legal under") && !problems.Contains("No ban list picked"));

            // ---- E: red badges and clickable problem lines ------------------------------------------------------------------
            var list = lists[Math.Max(0, newest)];
            var limited = _cardPack.All.Where(c => c.Alias == 0 && !c.IsExtraDeckMonster && list.Allowed(c.Code, _cardPack) == 1).Take(2).ToList();
            if (limited.Count < 2) Report($"two cards limited to 1 under {list.Name}", false);
            else
            {
                uint l1 = limited[0].Code, l2 = limited[1].Code, potCode = Code(pot);
                // Main: Pot, L1, L2, L1, Pot, L1, Pot: L1's first copy is Main 1.
                _editor.DebugSetDeck(new[] { potCode, l1, l2, l1, potCode, l1, potCode }, Array.Empty<uint>());
                yield return Frames(2);
                var badges = _editor.DebugProblemBadges();
                string Badge(int i) { var b = badges.FirstOrDefault(x => x.section == DeckSection.Main && x.index == i); return b.text == null ? "none" : $"{b.text} {b.color}"; }
                Report($"3 copies of {limited[0].Name} (limited to 1) wear a red 1 ({Badge(1)}, {Badge(3)}, {Badge(5)})",
                    new[] { 1, 3, 5 }.All(i => Badge(i) == $"1 {red}"));
                Report($"Forbidden {pot} wears a red X ({Badge(0)}, {Badge(4)}, {Badge(6)})", new[] { 0, 4, 6 }.All(i => Badge(i) == $"X {red}"));
                Report($"{limited[1].Name} within its limit keeps the amber number ({Badge(2)})", Badge(2) == $"1 {amber}");
                Report($"no badge counts as a change edge ({_editor.DebugEdges().Count} edges)", _editor.DebugEdges().Count == 0);
                // The size line isn't a link; the two card lines are.
                var links = _editor.DebugProblemLinks();
                var textLines = Plain(_editor.DebugProblemsText).Split('\n');
                int sizeLine = Array.FindIndex(textLines, l => l.Contains("Main Deck needs at least 40"));
                var linkLines = _editor.DebugProblemLinkLines();
                Report($"the problem lines that name a card are links ({string.Join(", ", links.Select(l => l.card))} on lines {string.Join(", ", linkLines)}: "
                       + $"{string.Join(" / ", linkLines.Select(i => i < textLines.Length ? textLines[i] : "?"))}); the size line ({sizeLine}) has no target",
                    links.Count == 2 && links.Any(l => l.card == limited[0].Name) && links.Any(l => l.card == pot) && sizeLine >= 0 && !linkLines.Contains(sizeLine)
                    && linkLines.All(i => i < textLines.Length && (textLines[i].Contains(limited[0].Name) || textLines[i].Contains(pot))));
                // Pointing underlines it; a click focuses the first copy (ring, left panel), not armed, and changes nothing.
                _editor.DebugSearch("");
                yield return Frames(3);
                yield return Click(R(2)); // the focus elsewhere first
                var deck0 = Deck();
                var at = _editor.DebugProblemLinkOnScreen(l1);
                if (at != null) Put(at.Value);
                yield return Frames(3);
                var underlined = _editor.DebugProblemLinks().FirstOrDefault(l => l.card == limited[0].Name).underlined;
                yield return Shot("problem-links");
                if (at != null) yield return Click(at.Value);
                var f = _editor.DebugFocus;
                yield return Shot("problem-links-focused"); // the ring on the card's first copy in the deck
                Report($"pointing at {limited[0].Name}'s line underlines it ({underlined}); a click focuses its first copy ({Focus()}; {_editor.DebugArmed}) and changes nothing",
                    at != null && underlined && f.zone == "Deck" && f.ring == "Main 1" && f.panel == limited[0].Name && _editor.DebugArmed == "not armed" && Deck() == deck0);
                Put(off);
                yield return Frames(2);
                Report($"pointed away, no line is underlined", !_editor.DebugProblemLinks().Any(l => l.underlined));
            }

            // ---- A: the mark, Save clears it, Back with no changes leaves at once ------------------------------------------
            _editor.DebugSetDeck(forty, Array.Empty<uint>());
            yield return Frames(2);
            bool markBefore = _editor.DebugUnsavedMark;
            Name("editor check plain");
            yield return Frames(2);
            bool markAfter = _editor.DebugUnsavedMark;
            yield return Shot("mark");
            yield return ClickButton("Save");
            var plainFile = DeckFile("editor check plain");
            Report($"a change shows the Save button's dot (before {markBefore}, after {markAfter}); Save saves ({File.Exists(plainFile)}) and the dot goes ({_editor.DebugUnsavedMark})",
                !markBefore && markAfter && File.Exists(plainFile) && !_editor.DebugUnsavedMark);
            Report("saved without a list picked by hand, the deck has no banlist note", File.Exists(plainFile) && !File.ReadAllText(plainFile).Contains("#banlist"));
            Name("editor check c"); // a second deck without a note (for the B / C steps)
            yield return ClickButton("Save");
            var cFile = DeckFile("editor check c");
            yield return ClickButton("Back");
            Report($"Back with nothing unsaved leaves at once (open {_editor.IsOpen}, asked {_editor.DebugLeaveDialogOpen})", !_editor.IsOpen && !_editor.DebugLeaveDialogOpen);
            yield return Frames(3);

            // ==== B: note-less decks, a list picked by hand, a list remembered ===========================================
            _editor.Open(null);
            yield return Frames(3);
            _editor.DebugBanlist(b => b.Name == Banlist.None.Name); // picked by hand in this opening
            _editor.Close();
            yield return Frames(2);
            _editor.Open("editor check plain");
            yield return Frames(3);
            Report($"a saved deck without a note opens on the newest TCG list, whatever the last opening showed ({_editor.BanlistName})", _editor.BanlistName == newestName);
            var goat = _editor.DebugBanlist(b => b.Whitelist);
            _editor.DebugOpenFromList("editor check plain");
            yield return Frames(3);
            Report($"a list picked by hand ({goat}) survives opening a note-less deck from the list ({_editor.BanlistName}, asked {_editor.DebugLeaveDialogOpen})",
                goat != null && _editor.BanlistName == goat && !_editor.DebugLeaveDialogOpen && _editor.CurrentDeckName == "editor check plain");
            var april = _editor.DebugBanlist(b => b.Name == "TCG April 2004");
            yield return Press(Key.LeftCtrl, Key.S);
            var noted = File.Exists(plainFile) ? File.ReadAllLines(plainFile).FirstOrDefault(l => l.StartsWith("#banlist")) ?? "" : "";
            Report($"Ctrl+S saves the deck with the list picked by hand (\"{noted}\"; status \"{_editor.DebugDeck.status}\")",
                april != null && noted == $"#banlist {april}" && _editor.DebugDeck.status.StartsWith("Saved to"));
            _editor.Close();
            _editor.Open(null);
            yield return Frames(2);
            _editor.DebugBanlist(b => b.Name == Banlist.None.Name);
            _editor.Close();
            _editor.Open("editor check plain");
            yield return Frames(3);
            Report($"in a new opening the deck reopens under that list ({_editor.BanlistName})", _editor.BanlistName == april);
            // E: a card not out yet under that list wears a red "!".
            var ash = _cardPack.All.FirstOrDefault(c => c.Alias == 0 && c.Name.StartsWith("Ash Blossom"));
            if (ash != null)
            {
                _editor.DebugSetDeck(forty.Take(39).Append(ash.Code), Array.Empty<uint>());
                yield return Frames(2);
                var b = _editor.DebugProblemBadges().FirstOrDefault(x => x.section == DeckSection.Main && x.index == 39);
                Report($"{ash.Name}, not out yet under {april}, wears a red \"!\" ({b.text} {b.color}); \"{Plain(_editor.DebugProblemsText).Split('\n').LastOrDefault()}\"",
                    b.text == "!" && b.color == red);
            }
            else Report("Ash Blossom is in the card data", false);
            // A historic deck opens under its era.
            var historic = _library.Names.Select(n => (name: n, deck: _library.Load(n))).FirstOrDefault(d => !_library.IsOwn(d.name) && d.deck.Note("era").Length > 0
                && lists.Any(l => l.Name == d.deck.Note("banlist")) && d.deck.Note("banlist") != newestName);
            if (historic.name != null)
            {
                _editor.DebugOpenFromList(historic.name);
                yield return Frames(3);
                Report($"the built-in historic deck {historic.name} opens under its era ({_editor.BanlistName}; its note {historic.deck.Note("banlist")})", _editor.BanlistName == historic.deck.Note("banlist"));
            }
            else Report("a built-in historic deck with a list", false);
            // Review fix 9: list X picked by hand, deck B (a note of its own) opened, then note-less deck C: C opens under X, and
            // saving C writes a note only when a list was picked while C was open.
            if (historic.name != null)
            {
                var x = _editor.DebugBanlist(b => b.Whitelist);
                _editor.DebugOpenFromList(historic.name);
                yield return Frames(2);
                var underB = _editor.BanlistName;
                _editor.DebugOpenFromList("editor check c");
                yield return Frames(2);
                Report($"picked {x} by hand, then {historic.name} (under {underB}), then a note-less deck: it opens under {x} ({_editor.BanlistName})",
                    x != null && underB == historic.deck.Note("banlist") && _editor.BanlistName == x);
                yield return Press(Key.LeftCtrl, Key.S);
                bool cNoted = File.ReadAllText(cFile).Contains("#banlist");
                Report($"saving it without touching the arrows writes no note (noted {cNoted}; \"{_editor.DebugDeck.status}\")", File.Exists(cFile) && !cNoted);
                var y = _editor.DebugBanlist(b => b.Name == Banlist.None.Name);
                yield return Press(Key.LeftCtrl, Key.S);
                var cNote = File.ReadAllLines(cFile).FirstOrDefault(l => l.StartsWith("#banlist")) ?? "";
                Report($"a list picked while it's open is written when it's saved (\"{cNote}\")", cNote == $"#banlist {y}");
            }
            // "No limits" with no problems: amber, and the new words.
            _editor.DebugBanlist(b => b.Name == Banlist.None.Name);
            _editor.DebugSetDeck(forty, Array.Empty<uint>());
            yield return Frames(2);
            var noLimits = _editor.DebugProblemsText;
            yield return Shot("no-limits");
            Report($"\"No limits\" with no problems is amber with the new words (\"{Plain(noLimits).Split('\n').LastOrDefault()}\")",
                noLimits.Contains($"<color={T.WarnHex}>No ban list picked (No limits): only deck size and 3 copies are checked. Pick a list with < >.</color>"));
            _editor.Close();
            yield return Frames(2);

            // ==== A: the leave question ====================================================================================
            _editor.Open(null);
            yield return Frames(3);
            _editor.DebugSetDeck(new[] { Code(sangan), Code(mst), Code(raigeki), Code(reborn) }, Array.Empty<uint>());
            Name("editor check a");
            yield return Frames(2);
            var aFile = DeckFile("editor check a");
            yield return Press(Key.LeftCtrl, Key.S);
            Report($"Ctrl+S saves (the file {File.Exists(aFile)}, the dot {_editor.DebugUnsavedMark})", File.Exists(aFile) && !_editor.DebugUnsavedMark && !_editor.DebugDirty);
            // Back with changes asks and leaves the editor open.
            _editor.DebugSearch("destroy");
            yield return Frames(3);
            var longText = _editor.DebugFocusLongText(3 * _editor.DebugCardDetail.NotchPixels + 10);
            yield return Frames(2);
            _editor.DebugSelect(Code(pot));
            yield return Frames(2);
            var deckA = Deck();
            var focusA = Focus();
            yield return ClickButton("Back");
            yield return Frames(2);
            Report($"Back with changes asks (\"{_editor.DebugLeaveDialogTitle}\") and the editor stays open ({_editor.IsOpen})",
                _editor.DebugLeaveDialogOpen && _editor.DebugLeaveDialogTitle == "Save changes to editor check a?" && _editor.IsOpen);
            yield return Shot("dialog");
            // Under it the focus keys, the wheel and the change message do nothing.
            float scroll0 = _editor.DebugCardDetail.DebugScroll.offset;
            int page0 = _editor.ResultsPage;
            foreach (var k in new[] { Key.RightArrow, Key.DownArrow, Key.Delete, Key.Backspace, Key.PageDown }) yield return Press(k);
            yield return Wheel(R(1), -1);
            yield return Wheel(_editor.DebugPartOnScreen("text"), -1);
            var pill = _editor.DebugPillBox;
            Report($"under the question the arrows, Delete, Backspace, Page Down and the wheel do nothing ({Focus()}; page {_editor.ResultsPage}; text {scroll0:0} -> {_editor.DebugCardDetail.DebugScroll.offset:0} px; {longText}) and the change message hides ({pill?.shown})",
                _editor.DebugLeaveDialogOpen && Deck() == deckA && Focus() == focusA && _editor.ResultsPage == page0 && Mathf.Abs(_editor.DebugCardDetail.DebugScroll.offset - scroll0) < 0.5f
                && longText != null && (pill == null || !pill.Value.shown));
            // A click on the scrim is Cancel.
            yield return Click(new Vector2(Screen.width * 0.1f, Screen.height * 0.5f));
            Report($"a click beside the panel cancels (asked {_editor.DebugLeaveDialogOpen}; deck kept {Deck() == deckA}; dot {_editor.DebugUnsavedMark})", !_editor.DebugLeaveDialogOpen && Deck() == deckA && _editor.DebugUnsavedMark && _editor.IsOpen);
            yield return Wheel(R(1), -1);
            Report($"with the question gone the wheel scrolls the text again ({scroll0:0} -> {_editor.DebugCardDetail.DebugScroll.offset:0} px)", _editor.DebugCardDetail.DebugScroll.offset > scroll0 + 5);
            // Cancel keeps the deck and the mark.
            yield return ClickButton("Back");
            yield return ClickButton("Cancel");
            Report($"Cancel keeps the deck and the dot (asked {_editor.DebugLeaveDialogOpen}; deck kept {Deck() == deckA}; dot {_editor.DebugUnsavedMark}; open {_editor.IsOpen})",
                !_editor.DebugLeaveDialogOpen && Deck() == deckA && _editor.DebugUnsavedMark && _editor.IsOpen);
            // The controller's B asks, and B again cancels.
            yield return PadPress(GamepadButton.East);
            bool padAsked = _editor.DebugLeaveDialogOpen;
            yield return PadPress(GamepadButton.East);
            Report($"the controller's B asks ({padAsked}), and B again cancels (asked {_editor.DebugLeaveDialogOpen}, open {_editor.IsOpen})", padAsked && !_editor.DebugLeaveDialogOpen && _editor.IsOpen);
            // Review fix 2: under the question LB / RB, Y and View do nothing.
            {
                _editor.DebugSearch("");
                yield return Frames(2);
                yield return ClickButton("Back");
                int pageQ = _editor.ResultsPage;
                bool favQ = _editor.DebugSelectedFavorite;
                yield return PadPress(GamepadButton.RightShoulder);
                int pageAfter = _editor.ResultsPage;
                yield return PadPress(GamepadButton.North);
                bool favAfter = _editor.DebugSelectedFavorite;
                yield return PadPress(GamepadButton.Select);
                bool moreAfter = _editor.DebugMoreOpen;
                Report($"under the question RB ({pageQ} -> {pageAfter}), Y (favorite {favQ} -> {favAfter}) and View (More open {moreAfter}) do nothing; the question stays ({_editor.DebugLeaveDialogOpen})",
                    _editor.DebugLeaveDialogOpen && pageAfter == pageQ && favAfter == favQ && !moreAfter);
                yield return ClickButton("Cancel");
                yield return PadPress(GamepadButton.RightShoulder);
                Report($"with the question gone RB turns the page again ({pageQ} -> {_editor.ResultsPage})", _editor.ResultsPage == pageQ + 1);
            }
            // Don't save leaves; no recovery copy is offered at the next opening.
            var aBefore = File.ReadAllText(aFile);
            yield return ClickButton("Back");
            yield return ClickButton("Don't save");
            Report($"Don't save leaves (open {_editor.IsOpen}), the saved deck as it was, no recovery file ({File.Exists(Recovery())})",
                !_editor.IsOpen && File.ReadAllText(aFile) == aBefore && !File.Exists(Recovery()));
            _editor.Open(null);
            yield return Frames(3);
            Report($"and the next opening offers nothing back ({_editor.DebugRecoveryOffered})", !_editor.DebugRecoveryOffered);
            _editor.Close();
            // Save saves the file and leaves.
            _editor.Open("editor check a");
            yield return Frames(3);
            _editor.DebugSelect(Code(pot));
            yield return ClickButton("Back");
            yield return ClickButton("Save");
            var potLine = Code(pot).ToString();
            Report($"Save saves the file (with {pot}: {File.ReadAllLines(aFile).Contains(potLine)}) and leaves (open {_editor.IsOpen})", File.ReadAllLines(aFile).Contains(potLine) && !_editor.IsOpen);
            // Enter is Save.
            _editor.Open("editor check a");
            yield return Frames(3);
            _editor.DebugSelect(Code(sangan));
            int sangans = File.ReadAllLines(aFile).Count(l => l == Code(sangan).ToString());
            yield return ClickButton("Back");
            yield return Press(Key.Enter);
            Report($"Enter in the question is Save ({sangans} -> {File.ReadAllLines(aFile).Count(l => l == Code(sangan).ToString())} {sangan} in the file; open {_editor.IsOpen})",
                File.ReadAllLines(aFile).Count(l => l == Code(sangan).ToString()) == sangans + 1 && !_editor.IsOpen);
            // New asks too; Don't save on New leaves no recovery file.
            _editor.Open("editor check a");
            yield return Frames(3);
            _editor.DebugSelect(Code(pot));
            bool recoveryBefore = File.Exists(Recovery());
            yield return ClickButton("New");
            bool newAsked = _editor.DebugLeaveDialogOpen;
            yield return ClickButton("Don't save");
            Report($"New with changes asks ({newAsked}); Don't save makes the new deck ({_editor.CurrentDeckName}) and leaves no recovery file (before {recoveryBefore}, after {File.Exists(Recovery())})",
                newAsked && recoveryBefore && _editor.CurrentDeckName == "new deck" && !File.Exists(Recovery()) && !_editor.DebugUnsavedMark);
            // A deck from the list asks too; Cancel keeps this deck and the list.
            _editor.DebugSelect(Code(raigeki));
            var deckNew = Deck();
            _editor.DebugOpenFromList("editor check a");
            yield return Frames(2);
            bool listAsked = _editor.DebugLeaveDialogOpen;
            yield return ClickButton("Cancel");
            Report($"a deck picked in Decks... asks ({listAsked}); Cancel keeps this deck ({_editor.CurrentDeckName}, unchanged {Deck() == deckNew})",
                listAsked && _editor.CurrentDeckName == "new deck" && Deck() == deckNew && !_editor.DebugLeaveDialogOpen);
            yield return Press(Key.Escape); // the list closes
            _editor.DebugOpenFromList("editor check a");
            yield return Frames(2);
            yield return ClickButton("Don't save");
            Report($"Don't save opens it ({_editor.CurrentDeckName}) with nothing unsaved ({_editor.DebugUnsavedMark})", _editor.CurrentDeckName == "editor check a" && !_editor.DebugUnsavedMark);
            _editor.Close();
            _editor.Open("editor check a");
            yield return Frames(3);
            Report($"after Don't save on a deck picked from the list, no recovery copy is offered ({_editor.DebugRecoveryOffered}; file {File.Exists(Recovery())})", !_editor.DebugRecoveryOffered && !File.Exists(Recovery()));
            // Save on a rebuilt deck opens Save's bar, drops the action and says so.
            _editor.DebugSetDeck(new[] { Code(pot), Code("Dark Hole"), Code("Heavy Storm"), Code("Swords of Revealing Light"), Code("Change of Heart") }, Array.Empty<uint>());
            _editor.DebugSelect(Code("Graceful Charity"));
            var aSaved = File.ReadAllText(aFile);
            yield return ClickButton("Back");
            yield return ClickButton("Save");
            bool bar = _editor.DebugTextFields().Any(t => t.name == "save-as name");
            Report($"Save on a rebuilt deck opens Save's bar ({bar}), drops the leaving (open {_editor.IsOpen}) and says so (\"{_editor.DebugDeck.status}\"); the file is as it was",
                bar && _editor.IsOpen && !_editor.DebugLeaveDialogOpen && _editor.DebugDeck.status == "Choose how to save, then leave." && File.ReadAllText(aFile) == aSaved);
            yield return Shot("save-bar");
            yield return Press(Key.Escape);
            Report($"Esc closes Save's bar ({!_editor.DebugTextFields().Any(t => t.name == "save-as name")}), the editor open", !_editor.DebugTextFields().Any(t => t.name == "save-as name") && _editor.IsOpen);
            _editor.DebugLeaveAnswer("cancel");
            _editor.DebugClearSignals();

            // Review fix 1: the Enter that answers the question reaches nothing else (it added the focused card to the next deck).
            {
                _editor.Close();
                _editor.Open("editor check a");
                yield return Frames(3);
                _editor.DebugSearch("dark magician");
                yield return Frames(3);
                yield return Click(R(1)); // a result focused: Enter alone would add it
                _editor.DebugSelect(Code(pot));
                yield return ClickButton("New");
                bool askedNew = _editor.DebugLeaveDialogOpen;
                yield return Press(Key.Enter);
                Report($"New, then Enter (Save) in the question: the new deck opens empty and not dirty (asked {askedNew}; {_editor.CurrentDeckName}, {_editor.CardCount(DeckSection.Main)} + {_editor.CardCount(DeckSection.Extra)} cards, dirty {_editor.DebugDirty})",
                    askedNew && _editor.CurrentDeckName == "new deck" && _editor.CardCount(DeckSection.Main) + _editor.CardCount(DeckSection.Extra) + _editor.CardCount(DeckSection.Side) == 0 && !_editor.DebugDirty);
                _editor.DebugSelect(Code(raigeki));
                _editor.DebugOpenFromList("editor check a");
                yield return Frames(2);
                bool askedList = _editor.DebugLeaveDialogOpen;
                yield return Press(Key.Enter);
                var aNow = File.ReadAllLines(aFile).Count(l => l.Length > 0 && char.IsDigit(l[0]));
                int inEditor = _editor.CardCount(DeckSection.Main) + _editor.CardCount(DeckSection.Extra) + _editor.CardCount(DeckSection.Side);
                Report($"a deck picked from the list, then Enter (Save): it opens as saved ({inEditor} cards, the file {aNow}) and not dirty ({_editor.DebugDirty}; asked {askedList}; {_editor.CurrentDeckName})",
                    askedList && _editor.CurrentDeckName == "editor check a" && inEditor == aNow && !_editor.DebugDirty);
                _editor.DebugSetDeck(new[] { Code("Dark Hole"), Code("Heavy Storm"), Code("Swords of Revealing Light"), Code("Change of Heart"), Code("Graceful Charity"), Code(sangan), Code(mst) }, Array.Empty<uint>());
                _editor.DebugSelect(Code(raigeki));
                yield return Frames(2);
                var rebuilt = Deck();
                yield return ClickButton("Back");
                yield return Press(Key.Enter);
                bool barNow = _editor.DebugTextFields().Any(t => t.name == "save-as name");
                Report($"Enter (Save) on a rebuilt deck opens Save's bar ({barNow}) and adds nothing (deck unchanged: {Deck() == rebuilt})", barNow && Deck() == rebuilt && _editor.IsOpen);
                yield return Press(Key.Escape);
                _editor.DebugClearSignals();
            }

            // ==== D: the keys ===============================================================================================
            _editor.Close();
            _editor.Open("editor check a");
            yield return Frames(3);
            int main = _editor.CardCount(DeckSection.Main);
            _editor.DebugSelect(Code(sangan));
            yield return Press(Key.LeftCtrl, Key.Z);
            int undone = _editor.CardCount(DeckSection.Main);
            yield return Press(Key.LeftCtrl, Key.Y);
            int redone = _editor.CardCount(DeckSection.Main);
            yield return Press(Key.LeftMeta, Key.Z);
            int undone2 = _editor.CardCount(DeckSection.Main);
            yield return Press(Key.LeftMeta, Key.LeftShift, Key.Z);
            Report($"Ctrl+Z undoes and Ctrl+Y redoes ({main + 1} -> {undone} -> {redone}); Cmd+Z and Cmd+Shift+Z too (-> {undone2} -> {_editor.CardCount(DeckSection.Main)})",
                undone == main && redone == main + 1 && undone2 == main && _editor.CardCount(DeckSection.Main) == main + 1);
            // Typed in the search box Ctrl+Z is the field's.
            var fields = _editor.DebugTextFields();
            var search = fields.First(t => t.name == "search").field;
            var nameBox = fields.First(t => t.name == "deck name").field;
            EventSystem.current.SetSelectedGameObject(search.gameObject);
            search.ActivateInputField();
            yield return Frames(3);
            var deckD = Deck();
            yield return Press(Key.LeftCtrl, Key.Z);
            Report($"Ctrl+Z while the search box has the keyboard changes nothing in the deck (typing {search.isFocused}; {Deck() == deckD})", search.isFocused && Deck() == deckD);
            search.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null!);
            yield return Frames(3);
            // Ctrl+F and "/" give the search box the keyboard.
            yield return Press(Key.LeftCtrl, Key.F);
            yield return Frames(3);
            bool byF = _editor.DebugSearchHasKeyboard;
            search.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null!);
            yield return Frames(3);
            yield return Press(Key.Slash);
            yield return Frames(3);
            bool bySlash = _editor.DebugSearchHasKeyboard;
            Report($"Ctrl+F ({byF}) and / ({bySlash}) give the search box the keyboard; / isn't typed into it (\"{search.text}\")", byF && bySlash && !search.text.Contains("/"));
            search.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null!);
            yield return Frames(3);
            // "/" typed in the deck name stays a slash there.
            EventSystem.current.SetSelectedGameObject(nameBox.gameObject);
            nameBox.ActivateInputField();
            yield return Frames(3);
            nameBox.MoveTextEnd(false);
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.Slash));
            nameBox.ProcessEvent(Event.KeyboardEvent("/"));
            nameBox.ForceLabelUpdate();
            yield return Frames(2);
            InputSystem.QueueStateEvent(keys, new KeyboardState());
            yield return Frames(3);
            Report($"/ typed in the deck name is a slash there (\"{nameBox.text}\"), the search box left alone ({_editor.DebugSearchHasKeyboard}; name typing {nameBox.isFocused})",
                nameBox.text.EndsWith("/") && !_editor.DebugSearchHasKeyboard && nameBox.isFocused);
            nameBox.DeactivateInputField();
            EventSystem.current.SetSelectedGameObject(null!);
            yield return Frames(3);
            // Review fix 3: shortcuts wait under overlays (Undo / Redo under the deck list and the test hand; Save in Save's bar;
            // Ctrl+F under the reading view).
            {
                var deckU = Deck();
                _editor.DebugBrowser();
                yield return Frames(2);
                yield return Press(Key.LeftCtrl, Key.Z);
                bool listKept = Deck() == deckU;
                _editor.DebugCloseBrowser();
                _editor.DebugShowTestHand();
                yield return Frames(2);
                yield return Press(Key.LeftCtrl, Key.Z);
                yield return Press(Key.LeftCtrl, Key.Y);
                bool handKept = Deck() == deckU;
                _editor.ControllerBack(); // closes the test hand
                Report($"Ctrl+Z under the deck list ({listKept}) and Ctrl+Z / Ctrl+Y under the test hand ({handKept}) change nothing", listKept && handKept);
                _editor.DebugAskHowToSave(true);
                yield return Frames(2);
                var saveAs = _editor.DebugTextFields().First(t => t.name == "save-as name").field;
                EventSystem.current.SetSelectedGameObject(saveAs.gameObject);
                saveAs.ActivateInputField();
                yield return Frames(3);
                saveAs.text = "my typed name";
                yield return Press(Key.LeftCtrl, Key.S);
                var stillBar = _editor.DebugTextFields().FirstOrDefault(t => t.name == "save-as name").field;
                Report($"Ctrl+S typed in Save's bar keeps the typed name (\"{stillBar?.text}\") and the bar", stillBar != null && stillBar.text == "my typed name");
                saveAs.DeactivateInputField();
                EventSystem.current.SetSelectedGameObject(null!);
                _editor.DebugAskHowToSave(false);
                yield return Frames(3);
                _editor.DebugReading();
                yield return Frames(2);
                yield return Press(Key.LeftCtrl, Key.F);
                yield return Frames(3);
                Report($"Ctrl+F under the reading view leaves the search box without the keyboard ({_editor.DebugSearchHasKeyboard}); the view stays ({_editor.DebugReadingOpen})", !_editor.DebugSearchHasKeyboard && _editor.DebugReadingOpen);
                _editor.DebugCloseReading();
                yield return Frames(3);
            }
            // Esc in a text field only leaves the field (Unity's own Esc puts its text back): the More panel stays.
            _editor.DebugToggleMore();
            yield return Frames(2);
            EventSystem.current.SetSelectedGameObject(search.gameObject);
            search.ActivateInputField();
            yield return Frames(3);
            search.text = "zzq";
            yield return Frames(2);
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.Escape));
            search.ProcessEvent(Event.KeyboardEvent("escape"));
            search.DeactivateInputField();
            yield return Frames(1);
            InputSystem.QueueStateEvent(keys, new KeyboardState());
            yield return Frames(3);
            Report($"Esc in the search box only leaves the field (typing {search.isFocused}, text \"{search.text}\"): the More panel stays open ({_editor.DebugMoreOpen}), the editor too ({_editor.IsOpen})",
                !search.isFocused && _editor.DebugMoreOpen && _editor.IsOpen);
            EventSystem.current.SetSelectedGameObject(null!);
            yield return Frames(3);
            // Esc closes the top thing, one at a time, then leaves (asking first with unsaved changes).
            _editor.DebugSearch("");
            _editor.DebugSelect(Code(pot)); // a change
            _editor.DebugShowTestHand();
            _editor.DebugReading();
            _editor.DebugBrowser();
            _editor.DebugAskHowToSave(true);
            _editor.DebugBack();
            yield return Frames(3);
            yield return Shot("esc-all-open");
            string Open() => $"{(_editor.DebugLeaveDialogOpen ? "question " : "")}{(_editor.DebugTextFields().Any(t => t.name == "save-as name") ? "save-bar " : "")}"
                             + $"{(_editor.DebugReadingOpen ? "reading " : "")}{(_editor.DebugBrowserOpen ? "list " : "")}{(_editor.TestHandOpen ? "hand " : "")}{(_editor.DebugMoreOpen ? "more " : "")}{(_editor.IsOpen ? "editor" : "closed")}";
            var seen = new List<string> { Open() };
            for (int i = 0; i < 7; i++)
            {
                yield return Press(Key.Escape);
                seen.Add(Open());
            }
            Report($"Esc closes one thing at a time, then asks before leaving: {string.Join(" | ", seen)}",
                seen.SequenceEqual(new[]
                {
                    "question save-bar reading list hand more editor", "save-bar reading list hand more editor", "reading list hand more editor",
                    "list hand more editor", "hand more editor", "more editor", "editor", "question editor",
                }));
            yield return ClickButton("Don't save");
            Report($"and Don't save leaves ({Open()})", !_editor.IsOpen);
            _editor.Open("editor check a");
            yield return Frames(3);
            yield return Press(Key.Escape);
            Report($"Esc with nothing open or unsaved leaves at once ({Open()})", !_editor.IsOpen);

            // ==== Review fix 8: the editor's Esc doesn't reach the duel board under it =====================================
            {
                var reader = _cardPack.All.First(c => c.Alias == 0 && c.Name == sangan);
                _view.OpenReader(reader); // a board overlay, as a Free Duel can have open when Decks... opens the editor
                yield return Frames(2);
                bool readerOpen = _view.ReaderOpen;
                _editor.Open(null);
                yield return Frames(3);
                yield return Press(Key.Escape); // nothing unsaved: the editor leaves
                yield return Frames(2);
                Report($"Esc that leaves the editor leaves the board's card reader open (open before {readerOpen}; editor {_editor.IsOpen}; reader {_view.ReaderOpen})",
                    readerOpen && !_editor.IsOpen && _view.ReaderOpen);
                yield return Frames(3);
                yield return Press(Key.Escape);
                Report($"with the editor shut, Esc closes the board's reader as before ({_view.ReaderOpen})", !_view.ReaderOpen);
            }

            // ==== The campaign editor: unchanged lists, its ownership line ==================================================
            _campaign.Start(CampaignState.Starters[0]);
            var profile = _campaign.Profile!;
            int dp = profile.Dp, total = profile.Collection.Total;
            int listed = Enumerable.Range(0, _campaign.Circuits.All.Count).FirstOrDefault(i => _campaign.CircuitList(i) != null);
            var circuitList = _campaign.CircuitList(listed)?.Name;
            _editor.Open(profile.Deck, EditorCollection(profile.Deck), circuitList, _campaign.CircuitTexts(listed));
            yield return new WaitForSecondsRealtime(0.5f);
            var campaignText = _editor.DebugProblemsText;
            Report($"the campaign editor opens on the circuit's list ({_editor.BanlistName}, the circuit's {circuitList}); legal reads green (\"{Plain(campaignText).Split('\n').LastOrDefault()}\")",
                circuitList != null && _editor.BanlistName == circuitList && !campaignText.Contains("No ban list picked")
                && campaignText.Contains($"<color={T.GoodHex}>This deck is legal under {circuitList}.</color>") && !campaignText.Contains("•"));
            yield return Click(R(0));
            Report($"the campaign editor still has an ownership line ({_editor.DebugFocus.panel}: {_editor.DebugOwnershipLine})", _editor.DebugOwnershipLine);
            _editor.Close();
            // Free play in between opens on the newest TCG list; the campaign opened with no list keeps its own last one.
            _editor.Open(null);
            yield return Frames(2);
            var freeBetween = _editor.BanlistName;
            _editor.Close();
            _editor.Open(profile.Deck, EditorCollection(profile.Deck), (string?)null);
            yield return Frames(3);
            Report($"opened with no list after free play ({freeBetween}), the campaign editor keeps its own last list ({_editor.BanlistName})",
                freeBetween == newestName && _editor.BanlistName == circuitList && circuitList != newestName);
            _editor.DebugBanlist(b => b.Name == Banlist.None.Name);
            yield return Frames(2);
            campaignText = _editor.DebugProblemsText;
            Report($"its \"No limits\" stays green (\"{Plain(campaignText).Split('\n').LastOrDefault()}\")", !campaignText.Contains("No ban list picked")
                && campaignText.Contains($"<color={T.GoodHex}>This deck is legal under No limits.</color>") && !campaignText.Contains("•"));
            _editor.Close();
            _campaign.Save();
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            Report($"no DP changed ({dp} -> {profile.Dp}) and no cards ({total} -> {profile.Collection.Total}); the save reloads", profile.Dp == dp && profile.Collection.Total == total);

            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keys);
            InputSystem.RemoveDevice(pad);
            Debug.Log($"editor check: done, {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
#endif
