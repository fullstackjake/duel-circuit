using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Champion's Ladder on the Library's shelf (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md section 3): "Past the binders",
    /// a plaque under the Every Era plaque, shown once the final is won (locked until Ilse is beaten once). Selected, the
    /// ladder board on the right: the ten rungs from the bottom up with their holders and decks, your rung marked, the next
    /// opponent highlighted, Ilse's sitting as four pips, a visitor's row over yours, and Duel and Scout on each row.
    /// </summary>
    public sealed partial class CampaignScreen
    {
        /// <summary>Duel pressed on a ladder seat (its key: "rung.3", "visitor.rue"). The screen has closed itself.</summary>
        public event Action<string>? LadderDuel;

        /// <summary>The ladder's place on the shelf: after the cups, the final's plaque and the Unlisted binder.</summary>
        private int LadderShelf => _state.Library.Cups.Count + 2;

        /// <summary>The ladder board is shown on the right.</summary>
        public bool LadderShown => _libraryView && _selectedLibrary == LadderShelf;

        /// <summary>Whether the plaque is on the shelf: the Every Era final won (locked or not).</summary>
        private bool LadderOnShelf(Profile profile) => _state.Ladder.IsShown(profile, _state.Library);

        /// <summary>The plaque's height on the shelf, and the room it takes above the binders.</summary>
        private const float LadderPlaqueHeight = 60, LadderStep = 72;

        /// <summary>Where the binders start: under the final's plaque, and under the ladder's when it's there.</summary>
        private float ShelfTop(Profile profile) => PlaqueStep + 12 + (LadderOnShelf(profile) ? LadderStep : 0);

        /// <summary>For checks: the plaque's line as drawn, and the board's rows as drawn (top rung first).</summary>
        public string LadderPlaqueLine { get; private set; } = "";
        public List<string> LadderRows { get; } = new List<string>();

        /// <summary>The plaque under the Every Era plaque: the ladder's name, then locked, your rung and the next holder, or the top.</summary>
        private void LadderPlaque(Profile profile)
        {
            if (!LadderOnShelf(profile)) { LadderPlaqueLine = ""; return; }
            var T = UiTheme.Current;
            var ladder = _state.Ladder;
            var state = profile.Ladder;
            bool open = ladder.IsOpen(profile, _state.Library), selected = _selectedLibrary == LadderShelf;
            const float x = 30, w = 720;
            float y = PlaqueStep + 4;
            // theme: kept, the white selection frame (no frame role is white)
            if (selected) Ui.Image(Ui.Box(_circuitList, x - 4, y - 4, w + 8, LadderPlaqueHeight + 8), Color.white).raycastTarget = false;
            // A slate frame with a silver edge: the plaque past the binders, under the final's gold.
            var frame = Ui.Box(_circuitList, x, y, w, LadderPlaqueHeight);
            Ui.Image(frame, open ? Ui.Hex("#9AA6B8") : Ui.Hex("#4A4E56"));
            var plaque = Ui.Box(frame, 2, 2, w - 4, LadderPlaqueHeight - 4);
            Ui.Image(plaque, open ? Ui.Hex("#1E2530") : Ui.Hex("#22242A"));
            var title = Ui.Text(Ui.Box(plaque, 18, 4, 330, 30), "THE CHAMPION'S LADDER", 22, open ? Ui.Hex("#DCE4F0") : new Color(0.62f, 0.64f, 0.68f), TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.raycastTarget = false;
            // theme: kept, the plaque's cool line (Library material, no role)
            var tag = Ui.Text(Ui.Box(plaque, 18, 32, 330, 22), "Past the binders", 13, new Color(0.7f, 0.74f, 0.8f), TextAnchor.MiddleLeft);
            tag.fontStyle = FontStyle.Italic;
            tag.raycastTarget = false;
            int climbed = ladder.Climbed(state);
            var next = ladder.Next(profile, state, _state.Library);
            LadderPlaqueLine = !open ? $"Locked: {ladder.LockReason(profile, _state.Library)}"
                : ladder.HoldsTop(state) && next == null ? "Top: You"
                : next != null && next.IsVisitor ? $"Rung {climbed} of {ladder.Rungs.Count} · {next.Character.Name} is visiting"
                : $"Rung {climbed} of {ladder.Rungs.Count} · next: {(next != null ? SeatName(profile, next, false) : "")}";
            var line = Ui.Text(Ui.Box(plaque, 350, 4, w - 4 - 350 - 64, LadderPlaqueHeight - 12), LadderPlaqueLine, 15,
                !open ? T.TextDim : ladder.HoldsTop(state) ? T.GoldSoft : new Color(0.88f, 0.9f, 0.94f), TextAnchor.MiddleRight);
            line.resizeTextForBestFit = true;
            line.resizeTextMinSize = 11;
            line.resizeTextMaxSize = 15;
            line.raycastTarget = false;
            // The next holder's face (the top's while locked or once yours); a stranger's is a ? tile until they've been introduced.
            var shownSeat = next ?? (ladder.Rungs.Count > 0 ? ladder.Rungs[^1] : null);
            if (shownSeat != null) SeatFace(Ui.Box(plaque, w - 4 - 60, 0, 56, 56), profile, shownSeat, 34);
            Ui.Clickable(plaque, () =>
            {
                Sounds.Play("click");
                _selectedLibrary = LadderShelf;
                _status.text = open
                    ? "<b>The Champion's Ladder</b>: one duel a rung, every holder at full strength. Beat the holder above you to take their rung."
                    : $"<b>The Champion's Ladder</b>: locked. {ladder.LockReason(profile, _state.Library)}.";
                Refresh();
            }, style: HoverStyle.Tile);
        }

        /// <summary>
        /// The ladder board: its rules and rewards, the rungs from the top down to rung 1 (a visitor's row over yours), each
        /// with its holder, deck, state and Duel / Scout; your deck checked against the ladder's rules.
        /// </summary>
        private void ShowLadder()
        {
            var T = UiTheme.Current;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            var profile = _state.Profile!;
            var ladder = _state.Ladder;
            var library = _state.Library;
            var state = profile.Ladder;
            bool open = ladder.IsOpen(profile, library);
            int climbed = ladder.Climbed(state);
            bool top = ladder.HoldsTop(state);
            var next = ladder.Next(profile, state, library);
            var rulesAt = _state.LadderRulesCircuit;
            var rulesName = _state.Circuits.All.Count > rulesAt ? _state.Circuits.All[rulesAt].Name : "";
            var list = _state.LadderRules.List?.Name;

            var title = Ui.Text(Ui.Box(_detail, 30, 12, 700, 46), "THE CHAMPION'S LADDER", 32, T.LibraryGold, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(_detail, 30, 56, 940, 24), "Past the binders. One duel a rung: beat the holder above you to take theirs. Every holder plays at full strength.",
                14, T.TextMuted, TextAnchor.MiddleLeft);
            var standing = !open ? $"<color={T.WarnHex}>Locked: {ladder.LockReason(profile, library)}.</color>"
                : top ? $"<color={T.GoldHex}>Top: You.</color> Challenge any rung again: +{Ladder.RepeatWinDp} DP a win."
                : $"Your rung: <b>{climbed}</b> of {ladder.Rungs.Count}. First climb of a rung <color={T.GoldHex}>+{Ladder.FirstClimbDp} DP</color> · the top "
                  + $"<color={T.GoldHex}>+{ladder.Prize:N0} DP, +{Ladder.TopTokens} Card Tokens and a poster</color>";
            Ui.Text(Ui.Box(_detail, 30, 80, 940, 26), standing, 15, T.Text, TextAnchor.MiddleLeft).supportRichText = true;
            Ui.Text(Ui.Box(_detail, 30, 104, 940, 24), $"Rules: {rulesName}'s list{(list != null ? $", {list}," : "")} whatever your campaign's mode. Your own decks only, no loaners. A loss keeps your rung.",
                13, T.Warn, TextAnchor.MiddleLeft).supportRichText = true;

            // The rows, top rung first; a visitor's row goes right over your rung (or over the foot of the ladder).
            var rows = new List<LadderSeat>();
            for (int r = ladder.Rungs.Count; r >= 1; r--)
            {
                if (next != null && next.IsVisitor && r == climbed) rows.Add(next);
                rows.Add(ladder.Rungs[r - 1]);
            }
            if (next != null && next.IsVisitor && climbed == 0) rows.Add(next);
            const float rowsTop = 134, step = 52, rowH = 46;
            LadderRows.Clear();
            for (int k = 0; k < rows.Count; k++) LadderRow(rows[k], rowsTop + k * step, rowH, profile, open, climbed, next);
            // The foot of the ladder: where you stand before the first rung.
            float after = rowsTop + rows.Count * step;
            if (climbed == 0)
            {
                var foot = Ui.Text(Ui.Box(_detail, 30, after - 4, 940, 20), open ? "▲ You: at the foot of the ladder" : "The foot of the ladder", 13,
                    open ? T.Highlight : T.TextDim, TextAnchor.MiddleLeft);
                foot.fontStyle = FontStyle.Bold;
                LadderRows.Add(foot.text);
            }
            float deckTop = Math.Max(after + 18, 714);
            DrawYourDeck(deckTop, _state.LadderProblems(profile.Deck));
            // YOUR DECK is judged under the ladder's list, named beside the caption.
            Ui.Text(Ui.Box(_detail, 130, deckTop, 840, 26), list != null ? $"checked against {list} ({rulesName})" : $"checked against {rulesName}'s rules",
                13, T.TextFaint, TextAnchor.MiddleLeft);
        }

        /// <summary>One row of the board: the rung (or VISITOR), the holder, their deck(s), the row's state, Duel and Scout.</summary>
        private void LadderRow(LadderSeat seat, float y, float h, Profile profile, bool open, int climbed, LadderSeat? next)
        {
            var T = UiTheme.Current;
            var ladder = _state.Ladder;
            var state = profile.Ladder;
            bool beaten = Ladder.Beaten(state, seat), isNext = open && ReferenceEquals(seat, next);
            bool yours = !seat.IsVisitor && seat.Rung == climbed && climbed > 0;
            bool canDuel = open && ladder.CanChallenge(profile, state, _state.Library, seat);
            var row = Ui.Box(_detail, 30, y, 940, h);
            _ladderLastRow = row;
            Ui.Image(row, isNext ? T.RowPlayable : beaten ? T.RowWon : seat.Rung % 2 == 0 ? T.RowA : T.RowB);
            if (isNext || yours) Ui.Image(Ui.Box(row, 0, 0, 4, h), isNext ? T.Highlight : T.Good).raycastTarget = false;
            var badge = Ui.Text(Ui.Box(row, 8, 0, 52, h), seat.IsVisitor ? "VISIT" : seat.Rung.ToString(), seat.IsVisitor ? 12 : 22,
                seat.IsVisitor ? T.Warn : isNext ? T.Highlight : beaten ? T.Good : T.TextBody);
            badge.fontStyle = FontStyle.Bold;
            var face = Ui.Box(row, 62, 1, h - 2, h - 2);
            Ui.Image(face, T.Well);
            face.gameObject.AddComponent<RectMask2D>();
            SeatFace(face, profile, seat, 30);
            var name = Ui.Text(Ui.Box(row, 114, 1, 330, 24), SeatName(profile, seat, true), 16, T.Text, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 11;
            name.resizeTextMaxSize = 16;
            var decks = seat.IsSitting ? string.Join(", ", seat.Decks.Select(_state.DeckTitle)) : _state.DeckTitle(seat.Deck);
            var deckLine = Ui.Text(Ui.Box(row, 114, 24, 330, 20), seat.IsSitting ? $"One sitting, all four: {decks}" : decks, 12, T.TextFaint, TextAnchor.MiddleLeft);
            deckLine.resizeTextForBestFit = true;
            deckLine.resizeTextMinSize = 9;
            deckLine.resizeTextMaxSize = 12;
            // A sitting's pips: one per deck, gold once won in this sitting (all gold once the rung is taken).
            if (seat.IsSitting)
            {
                var (won, of) = ladder.SittingProgress(seat, state);
                for (int p = 0; p < of; p++)
                {
                    var pip = Ui.Box(row, 452 + p * 30, (h - 22) / 2, 26, 22);
                    Ui.Image(pip, p < won ? Ui.Hex("#D9B24C") : isNext && p == won ? Ui.Hex("#FFC940") : Ui.Hex("#3A3428")).raycastTarget = false;
                    if (isNext && p == won) Ui.Image(Ui.Box(pip, 2, 2, 22, 18), Ui.Hex("#2B2716")).raycastTarget = false;
                    var n = Ui.Text(pip, (p + 1).ToString(), 12, p < won ? Ui.Hex("#20180A") : isNext && p == won ? Ui.Hex("#FFC940") : new Color(1, 1, 1, 0.5f));
                    n.fontStyle = FontStyle.Bold;
                    n.raycastTarget = false;
                }
            }
            var deckNow = ladder.DeckFor(seat, state);
            string what = seat.IsVisitor ? (beaten ? "Beaten" : "Visiting: beat them to go on")
                : yours && ladder.HoldsTop(state) ? "Top: You"
                : yours ? "Your rung"
                : beaten ? "Yours"
                : isNext ? (seat.IsSitting ? $"Next: {_state.DeckTitle(deckNow)} ({ladder.SittingProgress(seat, state).won + 1} of {seat.Decks.Count})" : "Next")
                : open ? "Above you" : "";
            var stateText = Ui.Text(Ui.Box(row, 572, 0, 190, h), what, 13, isNext ? T.Gold : yours ? T.Good : beaten ? T.Good : T.TextDim, TextAnchor.MiddleCenter);
            stateText.resizeTextForBestFit = true;
            stateText.resizeTextMinSize = 9;
            stateText.resizeTextMaxSize = 13;
            if (open)
            {
                var key = seat.Key;
                var duel = Ui.BoxButton(row, 770, 6, 78, h - 12, beaten ? "Again" : "Duel", () => { Close(); LadderDuel?.Invoke(key); }, 15);
                duel.targetGraphic.color = isNext ? T.Primary : T.Button;
                duel.interactable = canDuel;
                if (!canDuel) duel.GetComponentInChildren<Text>().color = T.Text.WithAlpha(0.35f);
            }
            var scouted = new List<(string, string)> { (SeatName(profile, seat, false), deckNow) };
            Ui.BoxButton(row, 854, 6, 78, h - 12, "Scout", () => ScoutClicked?.Invoke(scouted), 14).targetGraphic.color = T.Button;
            LadderRows.Add($"{(seat.IsVisitor ? "visitor" : "rung " + seat.Rung)} | {SeatName(profile, seat, false)} | {deckNow} | {what}{(canDuel ? " | duel" : "")}");
        }

        /// <summary>A seat's holder as the board names them: "???" for the top's holder until their introduction (Ladder.Unmet).</summary>
        private string SeatName(Profile profile, LadderSeat seat, bool display) =>
            _state.Ladder.Unmet(profile, seat) ? "???" : display ? seat.Character.DisplayName : seat.Character.Name;

        /// <summary>A seat's portrait, or the ? tile (the guests' locked face) for a holder not met yet.</summary>
        private void SeatFace(RectTransform box, Profile profile, LadderSeat seat, int size)
        {
            if (!_state.Ladder.Unmet(profile, seat)) { Portraits.Place(box, seat.Character.Id); return; }
            var mark = Ui.Text(box, "?", size, UiTheme.Current.Text.WithAlpha(0.35f));
            mark.fontStyle = FontStyle.Bold;
            mark.raycastTarget = false;
        }

        /// <summary>The campaign screen on the ladder board (back from a ladder duel's versus or results); <paramref name="message"/> in the status line.</summary>
        public void OpenLadder(string? message = null)
        {
            if (_state.Profile is Profile p && LadderOnShelf(p) && p.LibraryOpen) SelectLibrary(LadderShelf);
            Open(message);
        }

        // ---- For the checks -------------------------------------------------------------------------------------------

        /// <summary>The Library tab with the ladder board on the right, as the plaque clicked (false: no plaque on the shelf).</summary>
        public bool DebugShowLadder()
        {
            Open();
            ShowDistrictView(District.Library);
            if (_state.Profile is not Profile p || !LadderOnShelf(p)) return false;
            _selectedLibrary = LadderShelf;
            Refresh();
            ScrollToSelected();
            return LadderShown;
        }

        /// <summary>Whether the shelf has the plaque, and its line.</summary>
        public (bool shown, string line) DebugLadderPlaque => (_state.Profile is Profile p && LadderOnShelf(p), LadderPlaqueLine);

        /// <summary>A row's Duel, as clicked (if it can be pressed).</summary>
        public void DebugLadderDuel(string key)
        {
            var p = _state.Profile;
            var seat = _state.Ladder.SeatByKey(key);
            if (p == null || seat == null || !_state.Ladder.CanChallenge(p, p.Ladder, _state.Library, seat)) return;
            Close();
            LadderDuel?.Invoke(key);
        }

        /// <summary>The board's rectangles in screen space (each row and the detail panel), for the layout check.</summary>
        public Rect DebugDetailRect => ScreenRect(_detail);

        /// <summary>The board's last row as drawn (the bottom one), for the layout check.</summary>
        private RectTransform? _ladderLastRow;

        /// <summary>
        /// For the layout check, in screen space (y up): the panel, the board's bottom row, the lowest thing drawn in the panel
        /// (YOUR DECK's verdict line), the status line under it, and every "Back" button on the screen (none on the campaign
        /// screen itself: the versus has its own).
        /// </summary>
        public (Rect panel, Rect lastRow, float contentBottom, Rect status, List<Rect> backs) DebugLadderGeometry()
        {
            Canvas.ForceUpdateCanvases();
            float bottom = float.MaxValue;
            var corners = new Vector3[4];
            foreach (var rt in _detail.GetComponentsInChildren<RectTransform>())
            {
                if (rt == _detail) continue;
                rt.GetWorldCorners(corners);
                bottom = Mathf.Min(bottom, corners[0].y);
            }
            var backs = _root.GetComponentsInChildren<Button>()
                .Where(b => b.gameObject.activeInHierarchy && b.GetComponentInChildren<Text>()?.text == "Back")
                .Select(b => ScreenRect((RectTransform)b.transform)).ToList();
            return (ScreenRect(_detail), _ladderLastRow != null ? ScreenRect(_ladderLastRow) : default, bottom, ScreenRect(_status.rectTransform), backs);
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
        }
    }
}
