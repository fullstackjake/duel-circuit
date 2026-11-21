#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Ygo.Engine;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-focuscheck &lt;folder&gt;` (absolute; with -mods &lt;empty folder&gt;): the deck editor's click to focus and its change
    /// signals (docs/dev/EDITOR-CLICK-FOCUS-SPEC-2026-11-19.md, Verification), through a test mouse and keyboard the UI treats as
    /// real ones. Each step fails on the editor before click to focus (where a click added or removed at once, hover showed
    /// the card, and nothing signalled a change). Screenshots of the ring, the messages, "(+1)" and the text hint.
    /// </summary>
    public sealed partial class DuelController
    {
        private IEnumerator FocusCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"focus check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"focus-{name}.png"));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mods") < 0)
            {
                Report("needs -mods <empty folder> (the editor writes its recovery copies there)", false);
                Application.Quit();
                yield break;
            }
            Debug.Log($"focus check: window {Screen.width}x{Screen.height}; Input System wheel: {InputSystem.settings.scrollDeltaBehavior} "
                      + $"(one notch reads {DeckEditor.WheelTicks(1):0.##} tick as 1, {DeckEditor.WheelTicks(120):0.##} as 120)");

            var mouse = InputSystem.AddDevice<Mouse>("FocusCheckMouse");
            var keys = InputSystem.AddDevice<Keyboard>("FocusCheckKeys");
            void Put(Vector2 at, bool left = false, bool right = false) =>
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left, left).WithButton(MouseButton.Right, right));
            IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
            // A click (press and release, moved `wobble` px while held), with Shift held when asked.
            IEnumerator Click(Vector2 at, bool right = false, bool shift = false, float wobble = 0)
            {
                if (shift) InputSystem.QueueStateEvent(keys, new KeyboardState(Key.LeftShift));
                Put(at);
                yield return Frames(2);
                Put(at, !right, right);
                yield return Frames(2);
                if (wobble != 0)
                {
                    Put(at + new Vector2(wobble / 2, 0), !right, right);
                    yield return Frames(1);
                    Put(at + new Vector2(wobble, 0), !right, right);
                    yield return Frames(1);
                }
                Put(at + new Vector2(wobble, 0));
                yield return Frames(1);
                if (shift) InputSystem.QueueStateEvent(keys, new KeyboardState());
                yield return Frames(1);
            }
            IEnumerator Press(Key key, bool shift = false)
            {
                InputSystem.QueueStateEvent(keys, shift ? new KeyboardState(Key.LeftShift, key) : new KeyboardState(key));
                yield return Frames(2);
                InputSystem.QueueStateEvent(keys, new KeyboardState());
                yield return Frames(2);
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
            // Something sampled every frame for a while (an animation's peak).
            IEnumerator Watch(float seconds, Action each)
            {
                for (var until = Time.realtimeSinceStartup + seconds; Time.realtimeSinceStartup < until;)
                {
                    each();
                    yield return null;
                }
            }
            var off = new Vector2(-50, -50);
            Vector2 R(int slot) => _editor.DebugResultOnScreen(slot) ?? off;
            Vector2 D(DeckSection s, int i) => _editor.DebugDeckOnScreen(s, i) ?? off;
            int Slot(string name) => _editor.DebugPageNames().IndexOf(name);
            int Count(DeckSection s) => _editor.CardCount(s);
            string Deck() { var d = _editor.DebugDeck.deck; return $"{string.Join(",", d.Main)}|{string.Join(",", d.Extra)}|{string.Join(",", d.Side)}"; }
            string Focus() { var f = _editor.DebugFocus; return $"{f.zone} {f.card} (ring {f.ring}; left: {f.panel})"; }
            uint Code(string name) => _cardPack!.All.First(c => c.Name == name && c.Alias == 0).Code;
            string LastSound() => _editor.DebugSounds.LastOrDefault() ?? "none";
            var T = UiTheme.Current;
            string good = ColorUtility.ToHtmlStringRGB(T.Good), badHex = ColorUtility.ToHtmlStringRGB(T.Bad), text = ColorUtility.ToHtmlStringRGB(T.Text);

            const string sangan = "Sangan", mst = "Mystical Space Typhoon", raigeki = "Raigeki", pot = "Pot of Greed", magician = "Dark Magician", reborn = "Monster Reborn";
            _editor.Open(null);
            yield return new WaitForSecondsRealtime(0.6f);

            // ---- On open: the first result is focused --------------------------------------------------------------------
            var opened = _editor.DebugFocus;
            var firstName = _editor.DebugPageNames().FirstOrDefault() ?? "";
            Report($"on open the first result ({firstName}) is focused and on the left ({Focus()})",
                opened.zone == "Results" && opened.ring == "result 0" && opened.card == firstName && opened.panel == firstName);
            yield return Shot("open");
            // Fix 4 (owner's rule): a focus the editor set isn't armed: the first click on the top-left result only arms it.
            {
                var deckOpen = Deck();
                yield return Click(R(0));
                var armed = _editor.DebugArmed;
                bool unchanged = Deck() == deckOpen;
                yield return Click(R(0));
                Report($"the first click after opening, on the focused top-left result, adds nothing and arms it ({armed}); the next click adds it ({(Deck() != deckOpen ? "added" : "nothing")})",
                    unchanged && armed == "R0" && Deck() != deckOpen);
                _editor.DebugUndo();
            }

            _editor.DebugBanlist(b => b.Name == Banlist.None.Name);
            _editor.DebugSetDeck(new[] { Code(sangan), Code(mst), Code(raigeki), Code(sangan), Code(pot), Code(pot), Code(pot), Code(reborn) }, Array.Empty<uint>());
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            yield return Click(R(0)); // the focus on the page's first card
            _editor.DebugClearSignals();
            yield return Frames(2);

            // ---- 1. Pointing at 10 cards leaves the left panel and the focus alone ------------------------------------------
            var panelBefore = _editor.DebugFocus.panel;
            var pointed = new List<Vector2>();
            for (int i = 1; i <= 5; i++) pointed.Add(R(i));
            for (int i = 0; i < 5; i++) pointed.Add(D(DeckSection.Main, i));
            foreach (var at in pointed)
            {
                Put(at);
                yield return new WaitForSecondsRealtime(0.3f); // twice the old 0.15 s settle
            }
            yield return Shot("hover");
            Report($"pointing at 10 cards (0.3 s each) leaves the left panel on {panelBefore} and the focus where it was ({Focus()})",
                _editor.DebugFocus.panel == panelBefore && _editor.DebugFocus.ring == "result 0");

            // ---- 2. A click focuses without changing the deck; the focused one again adds -----------------------------------
            int slot = Slot(magician);
            if (slot <= 0) slot = 2;
            var name = _editor.DebugPageNames()[slot];
            var deck0 = Deck();
            int home0 = Count(DeckSection.Main) + Count(DeckSection.Extra);
            yield return Click(R(slot));
            var f1 = _editor.DebugFocus;
            Report($"a click on {name} focuses it (ring, left panel) and changes nothing ({Focus()})", Deck() == deck0 && f1.card == name && f1.ring == $"result {slot}" && f1.panel == name);
            var ring = _editor.DebugRing;
            Report($"the result's ring: 3 px in {ring?.color} at {ring?.alpha:0.##} with a 2 px gap, outside the card",
                ring is { } r1 && r1.width == 3 && r1.gap == 2 && r1.left == 5 && r1.top == 5 && r1.color == text && r1.alpha >= 0.99f);
            yield return Shot("ring-result");

            _editor.DebugClearSounds();
            bool sawGreen = false;
            float grew = 0, pulsed = 0;
            yield return Click(R(slot));
            int home1 = Count(DeckSection.Main) + Count(DeckSection.Extra);
            int newSlot = _editor.DebugDeck.deck.Main.Count - 1;
            yield return Watch(0.2f, () =>
            {
                foreach (var e in _editor.DebugEdges()) if (e.where == $"Main {newSlot}" && e.color == good && e.alpha > 0.9f) { sawGreen = true; grew = Mathf.Max(grew, e.scale); }
                pulsed = Mathf.Max(pulsed, _editor.DebugResultTile(name).countScale);
            });
            yield return Shot("pill-add"); // the edge still on (held 0.25 s), the message in
            var pill = _editor.DebugPill;
            Report($"the focused card clicked again adds one ({home0} -> {home1}), the focus staying ({Focus()})", home1 == home0 + 1 && _editor.DebugFocus.ring == $"result {slot}");
            Report($"the add shows: a green edge on the new tile Main {newSlot} ({sawGreen}), scaled from up to {grew:0.00}; its result's count pulsed to {pulsed:0.00}",
                sawGreen && grew > 1.005f && pulsed > 1.005f);
            Report($"\"(+1)\" by the Main count ({_editor.DebugHeader(DeckSection.Main)})", _editor.DebugHeader(DeckSection.Main).Contains($"Main {home1} (+1)"));
            Report($"the message beside Main: \"{pill?.text}\" edged {pill?.edge}, with {pill?.button} (beside {pill?.section})",
                pill is { } p1 && p1.text == $"+ {name}" && p1.edge == good && p1.button == "Undo" && p1.section == "Main");
            Report($"the status line: \"{_editor.DebugDeck.status}\"; the sound: {LastSound()}",
                _editor.DebugDeck.status == $"Added {name} (1 of 3)." && LastSound() == "place 0.35 pitch 1.00 (interface volume)");

            // Quick changes merge.
            yield return Click(R(slot));
            Report($"the same card added again within 2 s merges the message (\"{_editor.DebugPill?.text}\")", _editor.DebugPill?.text == $"+ {name} ×2");

            // Its Undo puts the deck back.
            _editor.DebugClearSounds();
            yield return new WaitForSecondsRealtime(0.2f);
            var undoAt = _editor.DebugPillButtonOnScreen;
            var deckBeforeUndo = Deck();
            if (undoAt != null) yield return Click(undoAt.Value);
            pill = _editor.DebugPill;
            Report($"the merged message's Undo takes both adds back ({Count(DeckSection.Main) + Count(DeckSection.Extra)} cards, was {home0 + 2}) and says \"{pill?.text}\" with {pill?.button}; sound {LastSound()}",
                undoAt != null && Deck() != deckBeforeUndo && Count(DeckSection.Main) + Count(DeckSection.Extra) == home0
                && pill is { } p2 && p2.text == $"Undone: + {name} ×2" && p2.button == "Redo" && LastSound() == "move 0.30 pitch 0.85 (interface volume)");
            var redoAt = _editor.DebugPillButtonOnScreen;
            if (redoAt != null) yield return Click(redoAt.Value);
            Report($"its Redo does it again ({Count(DeckSection.Main) + Count(DeckSection.Extra)} cards; \"{_editor.DebugPill?.text}\"; sound {LastSound()})",
                Count(DeckSection.Main) + Count(DeckSection.Extra) == home0 + 2 && _editor.DebugPill?.text == $"Redone: + {name} ×2" && LastSound() == "move 0.30 pitch 1.10 (interface volume)");
            // Two Undos from the toolbar: back to the deck before the adds.
            _editor.DebugUndo();
            _editor.DebugUndo();
            yield return Frames(2);
            Report($"two Undos put the deck back as it was", Deck() == deck0);
            _editor.DebugClearSignals();
            yield return Frames(2);

            // ---- 3. A 3 px wobble still focuses ------------------------------------------------------------------------------
            int wobbleSlot = slot == 3 ? 4 : 3;
            var wobbleName = _editor.DebugPageNames().ElementAtOrDefault(wobbleSlot) ?? "";
            deck0 = Deck();
            yield return Click(R(wobbleSlot), wobble: 3);
            Report($"a click that moves 3 px while held focuses {wobbleName} and changes nothing ({Focus()})", Deck() == deck0 && _editor.DebugFocus.ring == $"result {wobbleSlot}");

            // ---- 4. Shift+click and right-click (results) ------------------------------------------------------------------
            int otherSlot = slot == 1 ? 2 : 1;
            var otherName = _editor.DebugPageNames()[otherSlot];
            home0 = Count(DeckSection.Main) + Count(DeckSection.Extra);
            yield return Click(R(otherSlot), shift: true);
            Report($"Shift+click adds {otherName} at once ({home0} -> {Count(DeckSection.Main) + Count(DeckSection.Extra)}), the focus staying on {wobbleName} ({Focus()})",
                Count(DeckSection.Main) + Count(DeckSection.Extra) == home0 + 1 && _editor.DebugFocus.ring == $"result {wobbleSlot}");
            int side0 = Count(DeckSection.Side);
            yield return Click(R(otherSlot), right: true);
            int side1 = Count(DeckSection.Side);
            yield return Click(R(otherSlot), right: true, shift: true);
            Report($"right-click and Shift+right-click add {otherName} to the Side Deck ({side0} -> {side1} -> {Count(DeckSection.Side)}), the focus staying ({Focus()})",
                side1 == side0 + 1 && Count(DeckSection.Side) == side0 + 2 && _editor.DebugFocus.ring == $"result {wobbleSlot}");
            _editor.DebugClearSignals();
            _editor.DebugSetDeck(new[] { Code(sangan), Code(mst), Code(raigeki), Code(sangan), Code(pot), Code(pot), Code(pot), Code(reborn) }, Array.Empty<uint>());
            _editor.DebugSearch("sangan");
            yield return Frames(3);

            // ---- 5. Deck cards: a click focuses, again takes that copy out; the card that slides in isn't focused --------------
            deck0 = Deck();
            yield return Click(D(DeckSection.Main, 0));
            var f5 = _editor.DebugFocus;
            ring = _editor.DebugRing;
            Report($"a click on a deck card focuses it and changes nothing ({Focus()}); ring {ring?.width} px, gap {ring?.gap} px",
                Deck() == deck0 && f5.zone == "Deck" && f5.ring == "Main 0" && f5.panel == sangan && ring is { } r5 && r5.width == 3 && r5.gap == 1);
            yield return Shot("ring-deck");
            _editor.DebugClearSounds();
            int main0 = Count(DeckSection.Main);
            float slid = 0;
            bool sawRed = false;
            yield return Click(D(DeckSection.Main, 0));
            yield return Watch(0.2f, () =>
            {
                slid = Mathf.Max(slid, _editor.DebugDeckTile(DeckSection.Main, 0).dx);
                foreach (var e in _editor.DebugEdges()) if (e.where == $"result {sangan}" && e.color == badHex) sawRed = true;
            });
            yield return Shot("pill-remove");
            var f6 = _editor.DebugFocus;
            pill = _editor.DebugPill;
            Report($"the focused copy clicked again is taken out ({main0} -> {Count(DeckSection.Main)}); the ring goes to the other {sangan}, not to {_editor.DebugDeckCard(DeckSection.Main, 0)} which slid in ({Focus()})",
                Count(DeckSection.Main) == main0 - 1 && f6.card == sangan && f6.ring == "Main 2" && _editor.DebugDeckCard(DeckSection.Main, 2) == sangan);
            Report($"the removal shows: the cards after it slide in (up to {slid:0} px), the result tile's red edge ({sawRed}), \"{_editor.DebugHeader(DeckSection.Main)}\", \"{pill?.text}\" edged {pill?.edge}; sound {LastSound()}; status \"{_editor.DebugDeck.status}\"",
                slid > 5 && sawRed && _editor.DebugHeader(DeckSection.Main).Contains("(−1)") && pill is { } p6 && p6.text == $"− {sangan}" && p6.edge == badHex && p6.button == "Undo"
                && LastSound() == "draw 0.30 pitch 0.90 (interface volume)" && _editor.DebugDeck.status == $"Took out {sangan}. Undo brings it back.");
            int main1 = Count(DeckSection.Main);
            yield return Click(D(DeckSection.Main, 0));
            Report($"a third click where it was focuses {_editor.DebugDeckCard(DeckSection.Main, 0)} and takes nothing out ({main1} -> {Count(DeckSection.Main)}; {Focus()})",
                Count(DeckSection.Main) == main1 && _editor.DebugFocus.ring == "Main 0");

            // Shift+click a copy that isn't focused: out at once, the focus staying on its card.
            main1 = Count(DeckSection.Main);
            var shifted = _editor.DebugDeckCard(DeckSection.Main, 1);
            yield return Click(D(DeckSection.Main, 1), shift: true);
            Report($"Shift+click takes {shifted} out at once ({main1} -> {Count(DeckSection.Main)}), the focus staying on {_editor.DebugFocus.card} ({Focus()})",
                Count(DeckSection.Main) == main1 - 1 && _editor.DebugFocus.ring == "Main 0");

            // Right-click and Shift+right-click: between Main and Side, the focus following the card.
            _editor.DebugClearSignals();
            yield return Frames(2);
            _editor.DebugClearSounds();
            main1 = Count(DeckSection.Main);
            side0 = Count(DeckSection.Side);
            var moved = _editor.DebugDeckCard(DeckSection.Main, 0);
            bool sawWhite = false;
            yield return Click(D(DeckSection.Main, 0), right: true);
            yield return Watch(0.2f, () => { foreach (var e in _editor.DebugEdges()) if (e.where == $"Side {side0}" && e.color == text) sawWhite = true; });
            yield return Shot("pill-move");
            pill = _editor.DebugPill;
            Report($"right-click moves {moved} to the Side Deck (Main {main1} -> {Count(DeckSection.Main)}, Side {side0} -> {Count(DeckSection.Side)}), the focus following it ({Focus()})",
                Count(DeckSection.Main) == main1 - 1 && Count(DeckSection.Side) == side0 + 1 && _editor.DebugFocus.ring == $"Side {side0}");
            Report($"the move shows: a white edge on it in the Side Deck ({sawWhite}), \"{pill?.text}\" beside {pill?.section}, \"{_editor.DebugHeader(DeckSection.Side)}\"; sound {LastSound()}",
                sawWhite && pill is { } p7 && p7.text == $"{moved} → Side" && p7.section == "Side" && _editor.DebugHeader(DeckSection.Side).Contains("(+1)")
                && _editor.DebugHeader(DeckSection.Main).Contains("(−1)") && LastSound() == "move 0.30 pitch 1.00 (interface volume)");
            yield return Click(D(DeckSection.Side, side0), right: true, shift: true);
            Report($"Shift+right-click moves it back to Main (Side {Count(DeckSection.Side)}, Main {Count(DeckSection.Main)}; {Focus()})",
                Count(DeckSection.Side) == side0 && Count(DeckSection.Main) == main1 && _editor.DebugFocus.zone == "Deck" && _editor.DebugFocus.ring.StartsWith("Main"));

            // ---- 6. Refused: a fourth copy ---------------------------------------------------------------------------------
            _editor.DebugClearSignals();
            _editor.DebugSearch("pot of greed");
            yield return Frames(3);
            int potSlot = Slot(pot);
            deck0 = Deck();
            _editor.DebugClearSounds();
            yield return Click(R(potSlot));
            float shook = 0;
            yield return Click(R(potSlot));
            yield return Watch(0.26f, () => shook = Mathf.Max(shook, Mathf.Abs(_editor.DebugResultTile(pot).dx)));
            yield return Shot("pill-refused");
            pill = _editor.DebugPill;
            Report($"a fourth {pot} is refused: the deck stays, the tile shook ({shook:0.0} px), \"{pill?.text}\" edged {pill?.edge} with no button beside {pill?.section}; sound {LastSound()}",
                Deck() == deck0 && shook > 1.5f && pill is { } p8 && p8.text == "You already have 3." && p8.edge == badHex && p8.button == null && p8.section == "Main"
                && LastSound() == "error 0.50 pitch 1.00 (interface volume)");

            // ---- 7. A bulk undo: no flashes ----------------------------------------------------------------------------------
            _editor.DebugClearSignals();
            int all = Count(DeckSection.Main) + Count(DeckSection.Extra) + Count(DeckSection.Side);
            _editor.DebugClearDeck();
            yield return Frames(2);
            _editor.DebugUndo();
            int edges = 0;
            yield return Watch(0.3f, () => edges = Mathf.Max(edges, _editor.DebugEdges().Count));
            Report($"Undo after Clear says \"{_editor.DebugPill?.text}\" with {_editor.DebugPill?.button}, flashes nothing ({edges} edges), and \"{_editor.DebugHeader(DeckSection.Main)}\"",
                _editor.DebugPill?.text == $"Undone: Clear ({all} cards)" && _editor.DebugPill?.button == "Redo" && edges == 0 && _editor.DebugHeader(DeckSection.Main).Contains($"(+{Count(DeckSection.Main)})"));
            yield return Shot("pill-undo-clear");
            _editor.DebugClearSignals();

            // ---- 8. Keys typed into every text field change nothing ---------------------------------------------------------
            _editor.DebugToggleMore();
            _editor.DebugAskHowToSave(true);
            yield return Frames(2);
            var fields = _editor.DebugTextFields();
            foreach (var (fieldName, field) in fields)
            {
                deck0 = Deck();
                var focus0 = Focus();
                EventSystem.current.SetSelectedGameObject(field.gameObject);
                field.ActivateInputField();
                yield return Frames(3);
                bool typing = field.isFocused;
                foreach (var k in new[] { Key.Enter, Key.NumpadEnter, Key.Delete, Key.Backspace, Key.RightArrow, Key.DownArrow, Key.PageDown, Key.S, Key.A, Key.Minus })
                    yield return Press(k);
                yield return Press(Key.Enter, shift: true);
                Report($"keys typed into the {fieldName} field (Enter, Delete, Backspace, arrows, Page Down, s, a, -, Shift+Enter) change nothing (typing: {typing}; {Focus()})",
                    typing && Deck() == deck0 && Focus() == focus0);
                field.DeactivateInputField();
                EventSystem.current.SetSelectedGameObject(null!);
                yield return Frames(3);
            }
            _editor.DebugAskHowToSave(false);
            _editor.DebugToggleMore();
            yield return Frames(3);
            Report($"the fields looked at: {string.Join(", ", fields.Select(f => f.name))}", fields.Count == 5);

            // ---- 9. Enter, Shift+Enter, Delete, Backspace, the arrows, Page Up / Down ----------------------------------------
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            yield return Click(R(1));
            var f9 = _editor.DebugFocus;
            home0 = Count(DeckSection.Main) + Count(DeckSection.Extra);
            yield return Press(Key.Enter);
            Report($"Enter with no field typing adds the focused {f9.card} ({home0} -> {Count(DeckSection.Main) + Count(DeckSection.Extra)})", Count(DeckSection.Main) + Count(DeckSection.Extra) == home0 + 1);
            side0 = Count(DeckSection.Side);
            yield return Press(Key.Enter, shift: true);
            Report($"Shift+Enter adds it to the Side Deck ({side0} -> {Count(DeckSection.Side)})", Count(DeckSection.Side) == side0 + 1);
            yield return Press(Key.RightArrow);
            var right1 = _editor.DebugFocus.ring;
            yield return Press(Key.DownArrow);
            var down1 = _editor.DebugFocus.ring;
            yield return Press(Key.LeftArrow);
            Report($"the arrows move the focus in the results (right: {right1}, down: {down1}, left: {_editor.DebugFocus.ring})",
                right1 == "result 2" && down1 == "result 7" && _editor.DebugFocus.ring == "result 6");
            var pageFocus = _editor.DebugFocus.card;
            yield return Press(Key.PageDown);
            var paged = (_editor.ResultsPage, _editor.DebugFocus.card, _editor.DebugFocus.ring);
            yield return Press(Key.PageUp);
            Report($"Page Down turns the page and keeps the focus ({paged.ResultsPage}, {paged.card}, ring {paged.ring}); Page Up brings its ring back ({_editor.ResultsPage}, {_editor.DebugFocus.ring})",
                _editor.DebugResults.Count > 25 ? paged.ResultsPage == 1 && paged.card == pageFocus && paged.ring == "none" && _editor.ResultsPage == 0 && _editor.DebugFocus.ring == "result 6"
                : paged.ResultsPage == 0);
            // In the deck: arrows, Delete, Backspace, Shift+Enter, Enter.
            yield return Click(D(DeckSection.Main, 0));
            yield return Press(Key.RightArrow);
            var deckRight = _editor.DebugFocus.ring;
            main0 = Count(DeckSection.Main);
            yield return Press(Key.Delete);
            int afterDelete = Count(DeckSection.Main);
            yield return Press(Key.Backspace);
            Report($"in the deck: right arrow ({deckRight}), Delete ({main0} -> {afterDelete}) and Backspace (-> {Count(DeckSection.Main)}) take the focused copy out",
                deckRight == "Main 1" && afterDelete == main0 - 1 && Count(DeckSection.Main) == main0 - 2);
            yield return Click(D(DeckSection.Main, 0));
            main0 = Count(DeckSection.Main);
            side0 = Count(DeckSection.Side);
            yield return Press(Key.Enter, shift: true);
            var movedTo = _editor.DebugFocus.ring;
            yield return Press(Key.Enter);
            // (Quick fixes G4, 2026-11-20: Enter on a Side Deck copy adds one more to the Side Deck, the section it's in.)
            Report($"in the deck: Shift+Enter moves the focused copy to the Side (Main {main0} -> {main0 - 1}, ring {movedTo}); Enter on that Side copy adds one more to the Side (Side {side0} -> {Count(DeckSection.Side)}, Main {Count(DeckSection.Main)})",
                movedTo == $"Side {side0}" && Count(DeckSection.Side) == side0 + 2 && Count(DeckSection.Main) == main0 - 1);

            // ---- 10. A search keeps the focus ---------------------------------------------------------------------------------
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            yield return Click(R(3));
            var kept = _editor.DebugFocus;
            _editor.DebugSearch("sangan");
            yield return Frames(3);
            var during = _editor.DebugFocus;
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            Report($"a search keeps the focus ({kept.card}: during another search \"{during.card}\" ring {during.ring}, left {during.panel}; after, ring {_editor.DebugFocus.ring})",
                during.card == kept.card && during.panel == kept.panel && during.ring == "none" && _editor.DebugFocus.ring == "result 3");

            // ---- 11. The wheel: the focused card's text from the results, the deck and the panel; not over what scrolls itself --
            _editor.DebugSearch("destroy");
            yield return Frames(2);
            var longText = _editor.DebugFocusLongText(3 * _editor.DebugCardDetail.NotchPixels + 10);
            yield return Frames(3);
            var detail = _editor.DebugCardDetail;
            var top = detail.DebugScroll;
            yield return Shot("text-hint");
            Report($"the text hint on {longText}: \"▼ more\" ({top.more}) and the bar ({top.bar}) while it runs on ({top.room:0} px more)", longText != null && top.more && top.bar);
            var strip = detail.DebugMoreStrip;
            Report($"G3: \"▼ more\" sits on its own strip in the box's colour, under the fade (shown {strip.shown}, {strip.height:0} px, opaque {strip.opaque})",
                strip.shown && strip.height >= 16 && strip.opaque);
            int someSlot = _editor.DebugFocus.ring == "result 0" ? 1 : 0;
            yield return Wheel(R(someSlot), -1);
            float o1 = detail.DebugScroll.offset, glow1 = detail.DebugScroll.glow;
            yield return Wheel(D(DeckSection.Main, 1), -1);
            float o2 = detail.DebugScroll.offset;
            yield return Wheel(_editor.DebugPartOnScreen("detail"), -1);
            float o3 = detail.DebugScroll.offset;
            yield return Shot("text-glow");
            Report($"one notch down over a result, a deck card and the card's picture scrolls its text ({top.offset:0} -> {o1:0} -> {o2:0} -> {o3:0} px), the box glowing ({glow1:0.00})",
                o1 > top.offset + 5 && o2 > o1 + 5 && o3 > o2 + 5 && glow1 > 0.1f);
            yield return Wheel(_editor.DebugPartOnScreen("text"), 1);
            float o4 = detail.DebugScroll.offset;
            Report($"over the text itself one notch up scrolls it once, by its own scroll, the same step as from elsewhere ({o3:0} -> {o4:0} px; a notch from elsewhere {o1 - top.offset:0} px)",
                o4 < o3 - 5 && Mathf.Abs((o3 - o4) - (o1 - top.offset)) < 1.5f);
            _editor.DebugToggleMore();
            yield return Frames(2);
            yield return Wheel(_editor.DebugPartOnScreen("more"), -1);
            float o5 = detail.DebugScroll.offset;
            _editor.DebugToggleMore();
            yield return Wheel(_editor.DebugPartOnScreen("active"), -1);
            float o6 = detail.DebugScroll.offset;
            Report($"over the More panel and the active chip row the text stays ({o4:0} -> {o5:0} -> {o6:0} px)", Mathf.Abs(o5 - o4) < 0.5f && Mathf.Abs(o6 - o4) < 0.5f);
            for (int i = 0; i < 12; i++) yield return Wheel(R(someSlot), -1);
            var bottom = detail.DebugScroll;
            Report($"at the end of the text \"▼ more\" goes ({bottom.offset:0} of {bottom.room:0} px; more {bottom.more})", !bottom.more && bottom.offset >= bottom.room - 1);
            // The reading view: the wheel and the keys leave the editor alone.
            _editor.DebugReading();
            yield return Frames(2);
            float o7 = detail.DebugScroll.offset;
            home0 = Count(DeckSection.Main) + Count(DeckSection.Extra);
            yield return Wheel(R(someSlot), 1);
            yield return Press(Key.Enter);
            Report($"with the reading view open the wheel ({o7:0} -> {detail.DebugScroll.offset:0} px) and Enter ({home0} -> {Count(DeckSection.Main) + Count(DeckSection.Extra)}) do nothing in the editor",
                _editor.DebugReadingOpen && Mathf.Abs(detail.DebugScroll.offset - o7) < 0.5f && Count(DeckSection.Main) + Count(DeckSection.Extra) == home0);
            _editor.DebugCloseReading();
            yield return Frames(2);

            // ---- 12. The test hand: look only ---------------------------------------------------------------------------------
            _editor.DebugShowTestHand();
            yield return Frames(3);
            deck0 = Deck();
            yield return Click(_editor.DebugHandOnScreen(1) ?? off);
            var hand = _editor.DebugFocus;
            ring = _editor.DebugRing;
            yield return Shot("ring-hand");
            yield return Click(_editor.DebugHandOnScreen(1) ?? off);
            yield return Click(_editor.DebugHandOnScreen(1) ?? off, shift: true);
            yield return Click(_editor.DebugHandOnScreen(1) ?? off, right: true);
            yield return Press(Key.Enter);
            yield return Press(Key.Delete);
            Report($"a test hand card: a click focuses it ({hand.zone} {hand.card}, ring on {hand.ring}, {ring?.width} px with a {ring?.gap} px gap); clicks, Enter and Delete then change nothing",
                hand.zone == "Hand" && hand.ring == "hand 1" && hand.panel == hand.card && ring is { } r12 && r12.width == 4 && r12.gap == 2 && Deck() == deck0);
            _editor.ControllerBack(); // closes the test hand
            yield return Frames(2);
            Report($"closing the test hand moves its focus to {hand.card}'s result, not armed ({Focus()}; {_editor.DebugArmed})",
                _editor.DebugFocus.zone == "Results" && _editor.DebugFocus.card == hand.card && _editor.DebugArmed == "not armed");

            // ==== Fix round (code review, 2026-11-19) =========================================================================
            _editor.DebugClearSignals();
            _editor.DebugSetDeck(new[] { Code(sangan), Code(mst), Code(raigeki), Code(pot), Code(reborn) }, Array.Empty<uint>());
            _editor.DebugSearch("pot of greed");
            yield return Frames(3);

            // Fix 1: every change signals; a Sort replaces the "+ Pot of Greed" message, and its Undo undoes the Sort.
            yield return Click(R(Slot(pot)));
            yield return Click(R(Slot(pot)));
            var beforeSort = Deck();
            var addPill = _editor.DebugPill?.text;
            var addStatus = _editor.DebugDeck.status;
            _editor.DebugSort();
            var sortStatus = _editor.DebugDeck.status;
            yield return new WaitForSecondsRealtime(0.25f);
            pill = _editor.DebugPill;
            yield return Shot("pill-bulk");
            var sorted = Deck();
            var sortAt = _editor.DebugPillButtonOnScreen;
            if (sortAt != null) yield return Click(sortAt.Value);
            Report($"Sort replaces \"{addPill}\" with \"{pill?.text}\" ({pill?.button}), and that Undo undoes the Sort, not the add (order back: {Deck() == beforeSort}; still {Count(DeckSection.Main)} cards)",
                addPill == $"+ {pot}" && pill?.text == "Sorted" && pill?.button == "Undo" && sorted != beforeSort && Deck() == beforeSort);
            Report($"G1: after Sort the status line names it (\"{sortStatus}\", not the add's \"{addStatus}\")", sortStatus == "Sorted." && addStatus.StartsWith($"Added {pot}"));
            int all2 = Count(DeckSection.Main) + Count(DeckSection.Extra) + Count(DeckSection.Side);
            _editor.DebugClearDeck();
            yield return new WaitForSecondsRealtime(0.2f);
            Report($"Clear signals too: \"{_editor.DebugPill?.text}\" with {_editor.DebugPill?.button}, \"{_editor.DebugHeader(DeckSection.Main)}\"; overlaps no button ({string.Join(", ", _editor.DebugPillOverlaps())})",
                _editor.DebugPill?.text == $"Cleared {all2} cards" && _editor.DebugPill?.button == "Undo" && _editor.DebugHeader(DeckSection.Main).Contains("(−") && _editor.DebugPillOverlaps().Count == 0);
            Report($"G1: and the status line says \"{_editor.DebugDeck.status}\"", _editor.DebugDeck.status == $"Cleared {all2} cards. Undo brings them back.");
            _editor.DebugUndo();
            yield return Frames(2);

            // Fix 2: the keys don't act under the test hand or the More panel.
            yield return Click(D(DeckSection.Main, 0));
            deck0 = Deck();
            var ringBeforeHand = _editor.DebugFocus.ring;
            _editor.DebugShowTestHand();
            yield return Frames(3);
            var focusHand = Focus();
            // G2: a deck card's ring isn't drawn under the test hand (it peeked out at the overlay's corner).
            Report($"G2: with the test hand open the focused deck card ({_editor.DebugFocus.card}, ring {ringBeforeHand} before) has no ring drawn ({_editor.DebugFocus.ring}, {(_editor.DebugRing == null ? "none" : "drawn")})",
                ringBeforeHand == "Main 0" && _editor.DebugFocus.zone == "Deck" && _editor.DebugFocus.ring == "none" && _editor.DebugRing == null);
            foreach (var k in new[] { Key.Delete, Key.Backspace, Key.Enter, Key.RightArrow, Key.DownArrow }) yield return Press(k);
            yield return Press(Key.Enter, shift: true);
            yield return Shot("hand-open");
            Report($"with the test hand open, Delete, Backspace, Enter, Shift+Enter and the arrows leave the deck and the focus ({Focus()})", Deck() == deck0 && Focus() == focusHand);

            // Fix 3: a message never sits over the test hand: an Extra Deck message hides under it, and Close closes it.
            yield return Click(_editor.DebugHandCloseOnScreen);
            Report($"G2: closed, the hand gives the ring back ({_editor.DebugFocus.ring})", _editor.DebugFocus.ring == ringBeforeHand);
            _editor.DebugSearch("blue-eyes ultimate dragon");
            yield return Frames(3);
            yield return Click(R(0));
            yield return Click(R(0));
            yield return new WaitForSecondsRealtime(0.2f);
            var extraPill = _editor.DebugPill;
            var extraOverlaps = _editor.DebugPillOverlaps();
            deck0 = Deck();
            _editor.DebugShowTestHand();
            yield return Frames(3);
            var hidden = _editor.DebugPillBox;
            yield return Shot("hand-open-extra-message");
            yield return Click(_editor.DebugHandCloseOnScreen);
            Report($"the Extra message (\"{extraPill?.text}\" beside {extraPill?.section}, over no button: {string.Join(", ", extraOverlaps)}) hides under the test hand "
                   + $"(shown {hidden?.shown}, takes the pointer {hidden?.takesPointer}); a click on Close closes the hand and undoes nothing (open: {_editor.TestHandOpen})",
                extraPill?.section == "Extra" && extraOverlaps.Count == 0 && hidden is { } h3 && !h3.shown && !h3.takesPointer && !_editor.TestHandOpen && Deck() == deck0);
            // And under the More panel: Enter and the arrows act on nothing hidden.
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            yield return Click(R(1));
            deck0 = Deck();
            focusHand = Focus();
            _editor.DebugToggleMore();
            yield return Frames(2);
            foreach (var k in new[] { Key.Enter, Key.RightArrow, Key.DownArrow, Key.Delete }) yield return Press(k);
            var underMore = Focus();
            _editor.DebugToggleMore();
            Report($"with More open, Enter, Delete and the arrows leave the deck and the focus ({underMore})", Deck() == deck0 && underMore == focusHand);

            // Fix 5: a deck card that moved isn't armed: B focused at slot 1, A (slot 0) right-clicked to the Side, a click at slot 0 doesn't take B out.
            _editor.DebugClearSignals();
            _editor.DebugSetDeck(new[] { Code(sangan), Code(mst), Code(raigeki), Code(pot), Code(reborn) }, Array.Empty<uint>());
            _editor.DebugSearch("raigeki");
            yield return Frames(3);
            yield return Click(D(DeckSection.Main, 1));
            var armedB = _editor.DebugArmed;
            yield return Click(D(DeckSection.Main, 0), right: true);
            int mainB = Count(DeckSection.Main);
            yield return Click(D(DeckSection.Main, 0));
            Report($"{mst} focused at slot 1 ({armedB}), {sangan} before it moved to the Side: a click at slot 0 on {_editor.DebugDeckCard(DeckSection.Main, 0)} takes nothing out ({mainB} -> {Count(DeckSection.Main)}) and arms it ({_editor.DebugArmed})",
                Count(DeckSection.Main) == mainB && _editor.DebugDeckCard(DeckSection.Main, 0) == mst && _editor.DebugArmed == "D0:0");
            // Fix 4: the last copy taken out: its result tile, focused by the editor, isn't armed.
            int rSlot = 1; // Raigeki is at slot 1 now (MST at 0)
            yield return Click(D(DeckSection.Main, rSlot));
            yield return Click(D(DeckSection.Main, rSlot));
            var afterLast = Focus();
            int mainR = Count(DeckSection.Main);
            yield return Click(R(Slot(raigeki)));
            Report($"after its last copy came out the focus is on {raigeki}'s result ({afterLast}); a click there adds nothing ({mainR} -> {Count(DeckSection.Main)}) and arms it ({_editor.DebugArmed})",
                afterLast.StartsWith($"Results {raigeki}") && Count(DeckSection.Main) == mainR && _editor.DebugArmed == $"R{Slot(raigeki)}");

            // Fix 6: messages merge only for the same card, kind and section.
            _editor.DebugClearSignals();
            _editor.DebugSearch("monster reborn", Ygo.Engine.DeckBuilding.CardSearch.SearchMode.NameOnly);
            yield return Frames(3);
            int mr = Slot(reborn);
            if (mr < 0) Debug.Log($"focus check: (the page: {string.Join(", ", _editor.DebugPageNames())})");
            yield return Click(R(mr));
            yield return Click(R(mr));
            var mainMsg = _editor.DebugPill;
            yield return Click(R(mr), right: true);
            var sideMsg = _editor.DebugPill;
            int sideN = Count(DeckSection.Side);
            yield return Click(D(DeckSection.Side, sideN - 1), right: true);
            var toMain = _editor.DebugPill;
            yield return Click(D(DeckSection.Main, Count(DeckSection.Main) - 1), right: true);
            var toSide = _editor.DebugPill;
            Report($"no merging across sections: \"{mainMsg?.text}\" by {mainMsg?.section}, then \"{sideMsg?.text}\" by {sideMsg?.section}; \"{toMain?.text}\", then \"{toSide?.text}\"",
                mainMsg?.text == $"+ {reborn}" && mainMsg?.section == "Main" && sideMsg?.text == $"+ {reborn}" && sideMsg?.section == "Side"
                && toMain?.text == $"{reborn} → Main" && toSide?.text == $"{reborn} → Side");
            _editor.DebugClearSignals();
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            int dm = Slot(magician) >= 0 ? Slot(magician) : 0;
            yield return Click(R(dm));
            int before3 = Count(DeckSection.Main);
            for (int i = 0; i < 3; i++) yield return Click(R(dm));
            var merged = _editor.DebugPill?.text;
            var undo3 = _editor.DebugPillButtonOnScreen;
            if (undo3 != null) yield return Click(undo3.Value);
            Report($"\"{merged}\": its Undo takes all three back ({before3} -> {before3 + 3} -> {Count(DeckSection.Main)}; \"{_editor.DebugPill?.text}\")",
                merged == $"+ {magician} ×3" && Count(DeckSection.Main) == before3 && _editor.DebugPill?.text == $"Undone: + {magician} ×3");

            // Fix 8: only a click (or the reading view) counts as viewed; the arrows and a drag don't.
            var viewedFront = _editor.DebugViewedFront;
            yield return Press(Key.RightArrow);
            yield return Press(Key.RightArrow);
            var arrowed = _editor.DebugFocus.card;
            Report($"the arrows move the focus ({arrowed}) without adding to Recently viewed (front: {_editor.DebugViewedFront}, was {viewedFront})",
                arrowed != viewedFront && _editor.DebugViewedFront == viewedFront);

            // Fix 9: mid-drag the keys and paging do nothing; the drag still lands.
            _editor.DebugClearSignals();
            yield return Frames(2);
            var dragFrom = D(DeckSection.Main, 0);
            var dragName = _editor.DebugDeckCard(DeckSection.Main, 0);
            deck0 = Deck();
            int page0 = _editor.ResultsPage;
            viewedFront = _editor.DebugViewedFront;
            Put(dragFrom);
            yield return Frames(2);
            Put(dragFrom, left: true);
            yield return Frames(2);
            for (int i = 1; i <= 4; i++) { Put(dragFrom + new Vector2(0, -15 * i), left: true); yield return Frames(1); }
            bool ghost = _editor.DebugGhostShown;
            foreach (var k in new[] { Key.Delete, Key.Enter, Key.PageDown, Key.Backspace }) yield return Press(k);
            bool still = Deck() == deck0 && _editor.ResultsPage == page0 && _editor.DebugGhostShown;
            var sideZone = _editor.DebugZoneOnScreen(DeckSection.Side);
            int sideD = Count(DeckSection.Side);
            for (int i = 1; i <= 6; i++) { Put(Vector2.Lerp(dragFrom + new Vector2(0, -60), sideZone, i / 6f), left: true); yield return Frames(1); }
            Put(sideZone);
            yield return Frames(3);
            Report($"mid-drag (ghost {ghost}) Delete, Enter, Page Down and Backspace change nothing ({still}); dropped on the Side the drag still lands (Side {sideD} -> {Count(DeckSection.Side)}, ghost gone: {!_editor.DebugGhostShown}); "
                   + $"the drag focused {_editor.DebugFocus.card} without adding it to Recently viewed (front {_editor.DebugViewedFront})",
                ghost && still && Count(DeckSection.Side) == sideD + 1 && !_editor.DebugGhostShown && _editor.DebugFocus.card == dragName && _editor.DebugViewedFront == viewedFront);

            // Fix 11 and the one-frame grace: Enter that ends the search box (or any box) adds nothing; it focuses the first result
            // and lets the box go, so the next Enter adds it. (The Input System test keyboard makes no IMGUI events, which is what
            // InputField reads: the check does what InputField.OnUpdateSelected does with a Return, onSubmit then
            // DeactivateInputField, in the frame before the key reaches the editor, the order where the grace matters.)
            IEnumerator EnterInto(UnityEngine.UI.InputField field)
            {
                EventSystem.current.SetSelectedGameObject(field.gameObject);
                field.ActivateInputField();
                yield return Frames(3);
                InputSystem.QueueStateEvent(keys, new KeyboardState(Key.Enter));
                field.ProcessEvent(Event.KeyboardEvent("return"));
                field.onSubmit.Invoke(field.text);
                field.DeactivateInputField();
                yield return Frames(1);
                InputSystem.QueueStateEvent(keys, new KeyboardState());
                yield return Frames(3);
            }
            _editor.DebugClearSignals();
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            yield return Click(R(3));
            var fieldsNow = _editor.DebugTextFields();
            var searchBox = fieldsNow.First(f => f.name == "search").field;
            var nameBox = fieldsNow.First(f => f.name == "deck name").field;
            home0 = Count(DeckSection.Main) + Count(DeckSection.Extra);
            yield return EnterInto(nameBox);
            Report($"Enter that ends the deck name box adds nothing (the one-frame grace: {home0} -> {Count(DeckSection.Main) + Count(DeckSection.Extra)})", Count(DeckSection.Main) + Count(DeckSection.Extra) == home0);
            EventSystem.current.SetSelectedGameObject(null!);
            yield return Frames(3);
            yield return EnterInto(searchBox);
            var entered = _editor.DebugFocus;
            bool selected = EventSystem.current.currentSelectedGameObject == searchBox.gameObject;
            Report($"Enter in the search box adds nothing ({home0} -> {Count(DeckSection.Main) + Count(DeckSection.Extra)}), focuses the first result ({entered.ring}, {_editor.DebugArmed}) and lets the box go (selected: {selected})",
                Count(DeckSection.Main) + Count(DeckSection.Extra) == home0 && entered.ring == "result 0" && !selected);
            yield return Press(Key.Enter);
            yield return Frames(3);
            Report($"the next Enter adds it ({home0} -> {Count(DeckSection.Main) + Count(DeckSection.Extra)}) and doesn't open the box again (typing: {searchBox.isFocused})",
                Count(DeckSection.Main) + Count(DeckSection.Extra) == home0 + 1 && !searchBox.isFocused);

            // Fix 12: the add's status counts against the ban list too.
            var tcg = _editor.DebugBanlist(b => b.Name.Contains("TCG") && !b.Whitelist && b.Allowed(Code(pot), _cardPack!) == 0);
            _editor.DebugSearch("pot of greed");
            yield return Frames(3);
            int potCopies = _editor.DebugDeck.deck.Main.Count(c => c == Code(pot));
            yield return Click(R(Slot(pot)));
            yield return Click(R(Slot(pot)));
            Report($"under {tcg} (Pot of Greed forbidden) the add says \"{_editor.DebugDeck.status}\"", tcg != null && _editor.DebugDeck.status == $"Added {pot} ({potCopies + 1}; {tcg} allows 0).");
            _editor.DebugBanlist(b => b.Name == Banlist.None.Name);

            // Fix 10: another deck (or closing the editor) leaves no message, "(+1)" or edge behind.
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            yield return Click(R(Slot(magician) >= 0 ? Slot(magician) : 0));
            yield return Click(R(Slot(magician) >= 0 ? Slot(magician) : 0));
            bool hadSignals = _editor.DebugPill != null && _editor.DebugHeader(DeckSection.Main).Contains("(+");
            _editor.DebugNewDeck();
            yield return Frames(2);
            Report($"a new deck clears the signals (had them: {hadSignals}; message {_editor.DebugPill?.text ?? "none"}, \"{_editor.DebugHeader(DeckSection.Main)}\", {_editor.DebugEdges().Count} edges)",
                hadSignals && _editor.DebugPill == null && !_editor.DebugHeader(DeckSection.Main).Contains("(") && _editor.DebugEdges().Count == 0);
            _editor.DebugSetDeck(new[] { Code(sangan), Code(mst), Code(raigeki), Code(sangan), Code(pot), Code(pot), Code(pot), Code(reborn) }, Array.Empty<uint>());

            // G5: a deck opened from the deck list while a message shows: the message goes, and its Undo can't change that deck.
            {
                _editor.DebugSearch("dark magician");
                yield return Frames(3);
                int g5 = Slot(magician) >= 0 ? Slot(magician) : 0;
                yield return Click(R(g5));
                yield return Click(R(g5));
                var shown = _editor.DebugPill?.text;
                var other = _library.Names.FirstOrDefault(n => n == "classic") ?? _library.Names.First();
                _editor.DebugOpenFromList(other);
                yield return Frames(2);
                bool asked = _editor.DebugLeaveDialogOpen;
                _editor.DebugLeaveAnswer("discard");
                yield return Frames(3);
                var openedDeck = Deck();
                _editor.DebugUndo();
                yield return Frames(2);
                Report($"G5: {other} opened from the deck list (asked first: {asked}) while \"{shown}\" showed: no message ({_editor.DebugPill?.text ?? "none"}), and Undo can't change it (\"{_editor.DebugDeck.status}\")",
                    shown != null && asked && _editor.CurrentDeckName == other && _editor.DebugPill == null && Deck() == openedDeck && _editor.DebugDeck.status == "Nothing to undo.");
                _editor.DebugNewDeck();
                _editor.DebugBanlist(b => b.Name == Banlist.None.Name);
                _editor.DebugSetDeck(new[] { Code(sangan), Code(mst), Code(raigeki), Code(sangan), Code(pot), Code(pot), Code(pot), Code(reborn) }, Array.Empty<uint>());
            }

            // ---- 13. Reduced motion -----------------------------------------------------------------------------------------
            FxSettings.ReducedMotionOverride = true;
            _editor.DebugClearSignals();
            _editor.DebugSearch("dark magician");
            yield return Frames(3);
            int rmSlot = Slot(magician) >= 0 ? Slot(magician) : 0;
            yield return Click(R(rmSlot));
            yield return Click(R(rmSlot));
            int rmTile = _editor.DebugDeck.deck.Main.Count - 1;
            float rmScale = 1, alphaAt05 = -1;
            bool goneAt08;
            var startRm = Time.realtimeSinceStartup;
            yield return Watch(0.5f, () =>
            {
                foreach (var e in _editor.DebugEdges()) if (e.where == $"Main {rmTile}") { rmScale = Mathf.Max(rmScale, e.scale); alphaAt05 = e.alpha; }
            });
            yield return new WaitForSecondsRealtime(0.35f);
            goneAt08 = !_editor.DebugEdges().Any(e => e.where == $"Main {rmTile}");
            int rmMain = Count(DeckSection.Main);
            float rmSlide = 0;
            yield return Click(D(DeckSection.Main, 0), shift: true);
            yield return Watch(0.2f, () => rmSlide = Mathf.Max(rmSlide, Mathf.Abs(_editor.DebugDeckTile(DeckSection.Main, 0).dx)));
            Report($"reduced motion: no scale ({rmScale:0.00}), the edge solid at 0.5 s ({alphaAt05:0.00}) and gone by 0.85 s ({goneAt08}), no slide ({rmSlide:0.0} px; Main {rmMain} -> {Count(DeckSection.Main)})",
                rmScale <= 1.0001f && alphaAt05 > 0.99f && goneAt08 && rmSlide < 0.5f && Count(DeckSection.Main) == rmMain - 1);
            _editor.DebugSearch("pot of greed");
            yield return Frames(3);
            potSlot = Slot(pot);
            yield return Click(R(potSlot));
            float rmShake = 0;
            bool rmRed = false;
            yield return Click(R(potSlot));
            yield return Watch(0.3f, () =>
            {
                rmShake = Mathf.Max(rmShake, Mathf.Abs(_editor.DebugResultTile(pot).dx));
                foreach (var e in _editor.DebugEdges()) if (e.where == $"result {pot}" && e.color == badHex && e.alpha > 0.99f) rmRed = true;
            });
            Report($"reduced motion: a refused card doesn't shake ({rmShake:0.0} px) and wears a red edge ({rmRed})", rmShake < 0.5f && rmRed);
            FxSettings.ReducedMotionOverride = null;
            // G5 (review of this check, 2026-11-19): the message is still there when Close runs, so the step needs Close to clear it.
            var beforeClose = _editor.DebugPill?.text;
            _editor.Close();
            yield return Frames(3);
            Report($"closing the editor with a message showing (\"{beforeClose}\") leaves none ({_editor.DebugPill?.text ?? "none"})", beforeClose != null && _editor.DebugPill == null);

            // Fix 12: a right stick that drifts (never at rest since the editor opened) doesn't scroll the text.
            {
                var pad = InputSystem.AddDevice<Gamepad>("FocusCheckPad");
                _editor.Open(null);
                _editor.DebugSearch("destroy");
                var drifting = _editor.DebugFocusLongText(3 * _editor.DebugCardDetail.NotchPixels + 10);
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(0, -0.6f) });
                yield return new WaitForSecondsRealtime(0.4f);
                float drift = _editor.DebugCardDetail.DebugScroll.offset;
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return Frames(3);
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(0, -0.6f) });
                yield return new WaitForSecondsRealtime(0.3f);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                yield return Frames(2);
                float pushed = _editor.DebugCardDetail.DebugScroll.offset;
                Report($"a right stick held off-centre since the editor opened doesn't scroll {drifting} ({drift:0} px); once it has rested, pushed it does ({pushed:0} px)",
                    drifting != null && drift < 0.5f && pushed > 5);
                InputSystem.RemoveDevice(pad);
                _editor.Close();
                yield return Frames(3);
            }

            // ---- 14. The campaign: a card you don't own focuses as usual; adding it is refused --------------------------------
            _campaign.Start(CampaignState.Starters[0]);
            var profile = _campaign.Profile!;
            int dp = profile.Dp, total = profile.Collection.Total;
            _editor.Open(profile.Deck, EditorCollection(profile.Deck), (string?)null);
            yield return new WaitForSecondsRealtime(0.5f);
            _editor.DebugScope(0);
            _editor.DebugSearch("dragon");
            yield return Frames(3);
            var names = _editor.DebugPageNames();
            int unowned = names.FindIndex(n => _cardPack!.All.FirstOrDefault(c => c.Name == n && c.Alias == 0) is CardInfo c && !profile.Collection.Owns(c.Code));
            if (unowned < 0)
            {
                // Owned cards come first in the campaign: the last page has the ones to discover.
                _editor.DebugPage((_editor.DebugResults.Count - 1) / 25);
                yield return Frames(3);
                names = _editor.DebugPageNames();
                unowned = names.FindIndex(n => _cardPack!.All.FirstOrDefault(c => c.Name == n && c.Alias == 0) is CardInfo c && !profile.Collection.Owns(c.Code));
            }
            var unownedName = unowned >= 0 ? names[unowned] : "";
            deck0 = Deck();
            yield return Click(R(unowned));
            var f14 = _editor.DebugFocus;
            ring = _editor.DebugRing;
            yield return Shot("ring-not-owned");
            yield return Click(R(unowned));
            pill = _editor.DebugPill;
            Report($"a card you don't own ({unownedName}) focuses with a full-brightness ring ({f14.ring}, alpha {ring?.alpha:0.##})", unowned >= 0 && f14.ring == $"result {unowned}" && ring is { } r14 && r14.alpha >= 0.99f);
            Report($"adding it is refused: \"{_editor.DebugDeck.status}\" and \"{pill?.text}\" with no button", Deck() == deck0
                && _editor.DebugDeck.status.StartsWith($"You don't own {unownedName} yet. Get it") && pill?.text == "You don't own it yet." && pill?.button == null);
            _editor.Close();
            _campaign.Save();
            _campaign.DebugReload();
            profile = _campaign.Profile!;
            Report($"no DP changed ({dp} -> {profile.Dp}) and no cards ({total} -> {profile.Collection.Total}); the save reloads", profile.Dp == dp && profile.Collection.Total == total);

            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keys);
            Debug.Log($"focus check: done, {bad} failed");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
#endif
