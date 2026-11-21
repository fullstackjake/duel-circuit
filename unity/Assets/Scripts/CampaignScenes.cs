using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The campaign's moments around a duel: a versus screen before it (who you face, their deck and their opening line)
    /// and a results screen after it (their reaction, DP earned, circuit progress, and anything that just unlocked).
    /// </summary>
    public sealed class CampaignScenes : MonoBehaviour
    {
        /// <summary>One side of the versus screen.</summary>
        public sealed class Side
        {
            public string Name = "", Title = "", Deck = "", DeckNote = "", Detail = "", Quote = "";
            public List<uint> Showcase = new List<uint>();
            /// <summary>Character ids whose portraits stand on this side (one, or two for a tag team).</summary>
            public List<string> Portraits = new List<string>();
            /// <summary>A label over the show-off cards ("CONTRABAND" in the alley), or none.</summary>
            public string ShowcaseLabel = "";
            /// <summary>
            /// A label over each show-off card by its position (No Limits: "UNBANNED" over a restored card, "CONTRABAND" over a
            /// pick, "" over the rest); set, it replaces <see cref="ShowcaseLabel"/>.
            /// </summary>
            public List<string> ShowcaseLabels = new List<string>();
            /// <summary>A chip at the bottom of the side (an alley stake: the card and its value), or none.</summary>
            public string Chip = "";
        }

        /// <summary>A scouting report on one opposing deck, as the Scout panel shows it.</summary>
        public sealed class Scout
        {
            public string Who = "", Deck = "", Plan = "", Counters = "", Turn = "";
            public List<string> Keys = new List<string>();
            /// <summary>Cards to build with against it, each with how many you own ("D.D. Crow (you have 2)").</summary>
            public List<string> Build = new List<string>();
        }

        /// <summary>
        /// The Stuck? advice's purchase (under its tips): own the deck it names. <see cref="Confirm"/> set: a second click on
        /// the same button within a few seconds buys it (it spends DP); else one click does. Not <see cref="Enabled"/>: dimmed,
        /// and a click says why (<see cref="Refused"/>).
        /// </summary>
        public sealed class StuckOffer
        {
            public string Label = "", Note = "", Confirm = "", ConfirmNote = "", Refused = "";
            public bool Enabled = true;
            public Action Act = () => { };
        }

        public sealed class Results
        {
            public string Headline = "", Quote = "", Speaker = "", Circuit = "";
            public bool Won;
            public List<(string what, int dp)> Earnings = new List<(string, int)>();
            public int TotalDp;
            /// <summary>One entry per match in the circuit: won at least once.</summary>
            public List<bool> Progress = new List<bool>();
            /// <summary>What the chips count and their header ("Round" and "FINAL PROGRESS" for the Every Era final).</summary>
            public string ProgressItem = "Match", ProgressTitle = "CIRCUIT PROGRESS";
            public string Record = "";
            /// <summary>Big news: circuit cleared, a circuit opened, new card shop sets, a new ban list.</summary>
            public List<string> Unlocks = new List<string>();
            /// <summary>EXP earned, the level now, and progress into it.</summary>
            public (int earned, int level, int into, int needed) Exp;
            /// <summary>An alley duel's stakes in place of the DP lines: what, how it went, in which color.</summary>
            public List<(string what, string value, Color color)> Stakes = new List<(string, string, Color)>();
        }

        private RectTransform _root = null!;
        private ICardDataProvider _cards = null!;

        /// <summary>The results last shown, for automated checks.</summary>
        public Results? LastResults;
        /// <summary>The versus screen's sides last shown, and the guests screen's rows, for automated checks.</summary>
        public (Side you, Side them)? LastVersus;
        public IReadOnlyList<GuestRow> LastGuestRows { get; private set; } = new List<GuestRow>();

        /// <summary>The deck line's width beside the deck arrows: it ends 70 px short of the "&lt;" (at 700 on a 1920 screen).</summary>
        private const float DeckLineWithArrows = 520;
        /// <summary>Where a side's "&lt;" deck arrow starts, from the side's left edge (1920 x 1080 layout).</summary>
        private const float DeckArrowAt = 620;
        private Text? _arrowsDeckLine;
        private float _arrowsDeckRight, _arrowsDeckGap;
        /// <summary>The side arrows: drawn this far above the line they step, this tall.</summary>
        private const float ArrowRise = 6, ArrowHeight = 44;
        /// <summary>
        /// For checks: where the lowest "&lt;" / "&gt;" button on the versus ends, as drawn, and where your side's detail (under the
        /// deck line) starts, both from the screen's top in 1920 x 1080 layout px.
        /// </summary>
        public (float arrowsBottom, float detailTop) VersusArrowsClear
        {
            get
            {
                var corners = new Vector3[4];
                _root.GetWorldCorners(corners);
                float top = corners[1].y, scale = (corners[1].y - corners[0].y) / Mathf.Max(1f, _root.rect.height);
                float Layout(float worldY) => (top - worldY) / Mathf.Max(0.0001f, scale);
                float bottom = 0;
                foreach (var rt in _arrowRects)
                {
                    if (rt == null) continue;
                    rt.GetWorldCorners(corners);
                    bottom = Mathf.Max(bottom, Layout(corners[0].y));
                }
                float detail = _youDetail != null ? Layout(WorldTop(_youDetail.rectTransform)) : -1;
                return (bottom, detail);
            }
        }
        private Text? _youDetail;
        private readonly List<RectTransform> _arrowRects = new List<RectTransform>();
        private static float WorldTop(RectTransform rt) { var c = new Vector3[4]; rt.GetWorldCorners(c); return c[1].y; }

        /// <summary>
        /// For checks: the last versus's deck line beside the arrows: its text, where its box ends and the room left to the
        /// "&lt;" arrow (layout px), the size it came out at, and whether all of it fits its box (none cut off). Read a frame
        /// after drawing. Null when that versus had no deck arrows.
        /// </summary>
        public (string text, float right, float gap, int size, bool whole)? VersusDeckFit
        {
            get
            {
                if (_arrowsDeckLine == null) return null;
                var t = _arrowsDeckLine;
                var plain = System.Text.RegularExpressions.Regex.Replace(t.text, "<.*?>", "");
                int size = t.cachedTextGenerator.fontSizeUsedForBestFit > 0
                    ? Mathf.RoundToInt(t.cachedTextGenerator.fontSizeUsedForBestFit / Mathf.Max(0.01f, t.pixelsPerUnit)) : t.fontSize;
                var box = t.rectTransform.rect;
                bool whole = CardView.TextHeight(t, plain, size, box.width) <= box.height + 0.5f;
                return (plain, _arrowsDeckRight, _arrowsDeckGap, size, whole);
            }
        }

        /// <summary>The last results screen's EXP line under the bar, for automated checks.</summary>
        public string ExpLine { get; private set; } = "";

        /// <summary>"120 / 450 EXP to level 7"; "Max level" at the level cap (the EXP past it leads nowhere).</summary>
        public static string ExpToNext(int level, int into, int needed) =>
            level >= Leveling.MaxLevel ? "Max level" : $"{into} / {needed} EXP to level {level + 1}";

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        /// <summary>The last versus screen's texts line ("" when none of your originals changes text there), for checks.</summary>
        public string TextsLine { get; private set; } = "";
        /// <summary>For automated checks: the buttons of the screen showing, by label.</summary>
        public Dictionary<string, Action> Buttons { get; } = new Dictionary<string, Action>();

        public void Init(ICardDataProvider cards)
        {
            _cards = cards;
            _root = Ui.Overlay(transform, "CampaignScenes", 14);
            _root.gameObject.SetActive(false);
        }

        public void Close() => _root.gameObject.SetActive(false);

        private RectTransform Fresh()
        {
            for (int i = _root.childCount - 1; i >= 0; i--) Destroy(_root.GetChild(i).gameObject);
            Buttons.Clear();
            _arrowRects.Clear();
            _youDetail = null;
            _arrowsDeckLine = null;
            _scouting = null;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            Ui.Image(Ui.Stretched("Background", _root, Vector2.zero, Vector2.one), UiTheme.Current.Deep);
            return _root;
        }

        private void Button(float x, float y, float w, float h, string label, Action onClick, int size = 22, Color? color = null)
        {
            Action act = () => { Close(); onClick(); };
            var button = Ui.BoxButton(_root, x, y, w, h, label, act, Mathf.Max(UiTheme.Mapped(size), UiTheme.Size(TextRole.Label)));
            button.targetGraphic.color = color ?? UiTheme.Current.Button;
            Buttons[label] = act;
        }

        // ---------------------------------------------------------------- Versus

        /// <summary>
        /// Before a duel: your side against theirs. <paramref name="scouts"/> adds a Scout button (a report on each of their
        /// decks); <paramref name="stuck"/>, when not empty, puts that advice on top of it (after a run of losses), and opens it at
        /// once unless <paramref name="quiet"/> (the screen shown again after the deck arrows).
        /// </summary>
        public void ShowVersus(string circuit, string match, Side you, Side them, Action start, Action back, Action<int>? cycleDeck = null,
            List<Scout>? scouts = null, List<string>? stuck = null, bool quiet = false, StuckOffer? offer = null, string note = "", string arrowsHint = "",
            string texts = "", (string label, Action click)? action = null)
        {
            var root = Fresh();
            var T = UiTheme.Current;
            LastVersus = (you, them);
            _arrowsDeckLine = null;
            Ui.Image(Ui.Box(root, 0, 0, 1920, 120), T.Base);
            Typography.Strong(Ui.Text(Ui.Box(root, 0, 18, 1920, 50), circuit.ToUpperInvariant(), TextRole.Title, T.Sign));
            Ui.Text(Ui.Box(root, 0, 68, 1920, 34), match, TextRole.Label, T.TextMuted);

            DrawSide(you, 80, Ui.Hex("#4C8DFF"), arrows: cycleDeck != null);
            DrawSide(them, 1060, Ui.Hex("#FF5A5A"));
            var vs = Ui.Text(Ui.Box(root, 860, 360, 200, 160), "VS", TextRole.Display, T.Text);
            Typography.Strong(vs);
            vs.gameObject.AddComponent<Outline>().effectColor = T.Scrim.WithAlpha(0.8f);

            // Originals in your deck this table plays as current text (docs/dev/CARD-VERSIONS-SPEC-2026-11-03.md 5.5): a note
            // under your side, never a refusal.
            TextsLine = texts;
            if (texts.Length > 0)
            {
                var line = Ui.Text(Ui.Box(root, 80, 904, 780, 30), texts, TextRole.Body, T.Warn, TextAnchor.MiddleLeft);
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = UiTheme.Floor;
                line.resizeTextMaxSize = UiTheme.Size(TextRole.Body);
            }

            Button(760, 940, 400, 80, "Duel!", start, 32, T.Primary);
            Button(80, 960, 180, 56, "Back", back, 20);
            // A fix the screen offers for your deck (the Alley's "Make it Alley-legal"), between Back and Duel!.
            if (action is { } extra) Button(280, 960, 320, 56, extra.label, extra.click, 20);
            // Your deck: your own campaign decks, then the recipes you can borrow (loaners).
            if (cycleDeck != null)
            {
                Arrow(80 + DeckArrowAt, 650, "<", () => cycleDeck(-1));
                Arrow(80 + DeckArrowAt + 80, 650, ">", () => cycleDeck(+1));
                // (After a purchase on the Stuck? panel, what it did, in its place.)
                var hint = Ui.Text(Ui.Box(root, 110, 836, 700, 40), note.Length > 0 ? note : arrowsHint.Length > 0 ? arrowsHint : "Arrows: switch decks, or borrow a recipe you've unlocked (a loaner win pays half DP).", TextRole.Small,
                    note.Length > 0 ? T.Good : T.TextFaint, TextAnchor.MiddleLeft);
                hint.supportRichText = true;
                hint.resizeTextForBestFit = true;
                hint.resizeTextMinSize = UiTheme.Floor; // two lines at the floor fit
                hint.resizeTextMaxSize = UiTheme.Size(TextRole.Small);
            }
            if (scouts != null && scouts.Count > 0)
            {
                void Open() => ShowScouting(scouts, stuck ?? new List<string>(), null, offer);
                Ui.BoxButton(root, 1660, 960, 180, 56, stuck != null && stuck.Count > 0 ? "Stuck? Scout" : "Scout", Open, TextRole.Label).targetGraphic.color = T.Button;
                Buttons["Scout"] = Open;
                if (stuck != null && stuck.Count > 0 && !quiet) Open();
            }
        }

        /// <summary>The scouting panel over the versus screen: a column per opposing deck, and advice when you're stuck.</summary>
        public bool ScoutingOpen => _scouting != null;
        private RectTransform? _scouting;

        /// <summary>
        /// The scouting panel on its own (from the campaign screen or the guests, before you choose to duel): Close goes
        /// <paramref name="back"/>. <paramref name="over"/>: on top of the screen showing now (the guests) instead of a fresh one.
        /// </summary>
        public void ShowScoutReport(List<Scout> scouts, Action back, bool over = false)
        {
            if (!over) Fresh();
            ShowScouting(scouts, new List<string>(), over ? null : () => { Close(); back(); });
        }

        private void ShowScouting(List<Scout> scouts, List<string> stuck, Action? closed, StuckOffer? offer = null)
        {
            var T = UiTheme.Current;
            if (_scouting != null) Destroy(_scouting.gameObject);
            var overlay = Ui.Stretched("Scouting", _root, Vector2.zero, Vector2.one);
            _scouting = overlay;
            Ui.Image(overlay, T.Scrim.WithAlpha(0.82f));
            // Sized to its content once the text is in (centered, Close at the bottom), up to the screen's height.
            const float MaxHeight = 1040;
            var panel = Ui.Box(overlay, 200, 20, 1520, MaxHeight);
            Ui.Image(panel, T.PanelDeep);
            float top = 24;
            if (stuck.Count > 0)
            {
                Typography.Strong(Ui.Text(Ui.Box(panel, 40, top, 1440, 40), "STUCK? HERE'S WHAT HELPS", TextRole.Heading, T.Gold, TextAnchor.MiddleLeft));
                // The first line says why it opened; the rest are the tips.
                var text = string.Join("\n", stuck.Select((s, i) => i == 0 ? s : "•  " + s));
                var tips = Ui.Text(Ui.Box(panel, 40, top + 44, 1440, 25 * stuck.Count + 10), text, TextRole.Body, T.TextBright, TextAnchor.UpperLeft);
                tips.supportRichText = true;
                top += 74 + 25 * stuck.Count;
                if (offer != null)
                {
                    OfferRow(panel, top, offer);
                    top += 84;
                }
            }
            Typography.Strong(Ui.Text(Ui.Box(panel, 40, top, 1440, 40), "SCOUTING REPORT", TextRole.Heading, T.Info, TextAnchor.MiddleLeft));
            top += 50;
            float width = (1440 - 40 * (scouts.Count - 1)) / (float)scouts.Count, bodyTop = top + 40, tallest = 0;
            var bodies = new List<Text>();
            for (int i = 0; i < scouts.Count; i++)
            {
                var s = scouts[i];
                float x = 40 + i * (width + 40);
                Typography.Strong(Ui.Text(Ui.Box(panel, x, top, width, 34), $"{s.Who}: {s.Deck}", TextRole.Subhead, T.Text, TextAnchor.MiddleLeft));
                var lines = new List<string>();
                if (s.Plan.Length > 0) lines.Add(s.Plan);
                if (s.Keys.Count > 0) lines.Add($"<color={T.GoldHex}>Watch for:</color> {string.Join(" · ", s.Keys)}");
                if (s.Counters.Length > 0) lines.Add($"<color={T.GoodHex}>What beats it:</color> {s.Counters}");
                if (s.Build.Count > 0) lines.Add($"<color={T.VioletSoftHex}>Build against it:</color> {string.Join(" · ", s.Build)}");
                if (s.Turn.Length > 0) lines.Add($"<color={T.InfoHex}>If you win the toss:</color> {s.Turn}");
                var body = Ui.Text(Ui.Box(panel, x, bodyTop, width, MaxHeight - bodyTop - 100), string.Join("\n\n", lines), TextRole.Body,
                    T.TextSoft, TextAnchor.UpperLeft);
                body.supportRichText = true;
                bodies.Add(body);
                tallest = Mathf.Max(tallest, body.preferredHeight);
            }
            float height = Mathf.Clamp(bodyTop + tallest + 120, 400, MaxHeight);
            panel.sizeDelta = new Vector2(1520, height);
            panel.anchoredPosition = new Vector2(200, -(1080 - height) / 2);
            foreach (var column in bodies) column.rectTransform.sizeDelta = new Vector2(width, height - bodyTop - 100);
            void CloseScouting()
            {
                if (_scouting != null) Destroy(_scouting.gameObject);
                _scouting = null;
                closed?.Invoke();
            }
            Ui.BoxButton(panel, 1520 / 2 - 110, height - 76, 220, 56, "Close", CloseScouting, TextRole.Label);
            Buttons["Close scouting"] = CloseScouting;
        }

        /// <summary>
        /// The Stuck? purchase row: a gold-edged secondary button (Duel! stays the screen's one red action) and a line beside
        /// it saying what it does. A purchase arms on the first click (gold, "Confirm") and goes through on the second, within
        /// <see cref="ConfirmSeconds"/>.
        /// </summary>
        private void OfferRow(RectTransform panel, float top, StuckOffer offer)
        {
            var T = UiTheme.Current;
            var row = Ui.Box(panel, 40, top - 6, 1440, 72);
            Ui.Image(row, T.RowB);
            Ui.Image(Ui.Box(row, 0, 0, 4, 72), T.Highlight).raycastTarget = false;
            var status = Ui.Text(Ui.Box(row, 416, 6, 1008, 60), offer.Note, TextRole.Small, T.TextSoft, TextAnchor.MiddleLeft);
            status.supportRichText = true;
            status.resizeTextForBestFit = true;
            status.resizeTextMinSize = 14;
            status.resizeTextMaxSize = UiTheme.Size(TextRole.Small);
            Button? button = null;
            Text? label = null;
            float armedAt = -1;
            void Show(bool armed)
            {
                armedAt = armed ? Time.realtimeSinceStartup : -1;
                button!.targetGraphic.color = armed ? T.Highlight : T.Button;
                label!.text = armed ? offer.Confirm : offer.Label;
                label.color = armed ? T.OnAccent : T.Text;
                status.text = armed ? offer.ConfirmNote : offer.Note;
            }
            void Click()
            {
                if (!offer.Enabled) { status.text = offer.Refused; return; }
                if (offer.Confirm.Length > 0 && (armedAt < 0 || Time.realtimeSinceStartup - armedAt > ConfirmSeconds))
                {
                    Show(true);
                    StartCoroutine(Disarm(armedAt));
                    return;
                }
                // (A double-click isn't a confirmation: the second click has to come a moment after the button turns gold.)
                if (offer.Confirm.Length > 0 && Time.realtimeSinceStartup - armedAt < ConfirmDelay) return;
                offer.Act();
            }
            System.Collections.IEnumerator Disarm(float armed)
            {
                yield return new WaitForSecondsRealtime(ConfirmSeconds);
                if (button != null && armedAt == armed) Show(false);
            }
            button = Ui.BoxButton(row, 16, 8, 380, 56, offer.Label, Click, TextRole.Label);
            label = button.GetComponentInChildren<Text>();
            var edge = button.targetGraphic.gameObject.AddComponent<Outline>();
            edge.effectColor = T.Highlight;
            edge.effectDistance = new Vector2(2, -2);
            if (!offer.Enabled) button.gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;
            Buttons["Own stuck deck"] = Click;
        }

        /// <summary>How long a purchase stays armed for its confirming click, and how soon that click may come.</summary>
        public const float ConfirmSeconds = 5f, ConfirmDelay = 0.4f;

        // ---------------------------------------------------------------- Coin toss

        /// <summary>
        /// Before a duel: the coin toss. The coin spins (it doesn't with Reduced motion), then whoever won chooses who
        /// takes the first turn: you with Go first / Go second, the other side's choice shown with Continue (it carries
        /// on by itself after a moment). <paramref name="done"/> gets whether you (the left side) go first. A best-of-three's
        /// later game has no toss of its own: the last game's loser chooses (<paramref name="title"/> "GAME 2", its
        /// <paramref name="subtitle"/>, and <paramref name="won"/> "lost game 1" for "won the toss").
        /// </summary>
        public void ShowCoinToss(string you, string them, bool youWin, bool themFirst, string hint, Action<bool> done,
            string title = "COIN TOSS", string subtitle = "The winner chooses who takes the first turn.", string won = "won the toss")
        {
            var root = Fresh();
            var T = UiTheme.Current;
            Ui.Image(Ui.Box(root, 0, 0, 1920, 120), T.Base);
            Typography.Strong(Ui.Text(Ui.Box(root, 0, 18, 1920, 50), title, TextRole.Title, T.Sign));
            Ui.Text(Ui.Box(root, 0, 68, 1920, 34), subtitle, TextRole.Label, T.TextMuted);
            // The coin: art/coin's two faces (blue for the left side, red for the right, like the board), or a plain
            // gold coin marked Y and T without them. It flies up and flips end over end inside its box.
            var coin = Ui.Box(root, 830, 190, 260, 260);
            var (blueFace, redFace) = CoinFaces();
            var flier = Ui.Placed("Coin", coin, Vector2.zero, new Vector2(260, 260));
            RawImage? art = null;
            Text? face = null;
            if (blueFace != null && redFace != null)
            {
                art = flier.gameObject.AddComponent<RawImage>();
                art.texture = blueFace;
            }
            else
            {
                flier.sizeDelta = new Vector2(200, 200);
                Ui.Image(flier, Ui.Hex("#D8A93A"));
                var edge = flier.gameObject.AddComponent<Outline>();
                edge.effectColor = Ui.Hex("#7A5A12");
                edge.effectDistance = new Vector2(4, -4);
                face = Ui.Text(flier, "?", TextRole.Display, Ui.Hex("#5A3F0A"));
                Typography.Strong(face);
            }
            void ShowFace(bool left)
            {
                if (art != null) art.texture = left ? blueFace : redFace;
                if (face != null) face.text = left ? "Y" : "T";
            }
            var result = Ui.Text(Ui.Box(root, 260, 500, 1400, 60), "", TextRole.Title, T.Text);
            Typography.Strong(result);
            var detail = Ui.Text(Ui.Box(root, 260, 566, 1400, 90), "", TextRole.Label, T.TextSoft);
            detail.supportRichText = true;
            const string First = "Going first: set up before they can act (no draw and no attack on turn 1).";
            const string Second = "Going second: a sixth card, and the first chance to attack and break their setup.";
            // Once only: a click and the automatic Continue mustn't both start the duel.
            bool chosen = false;
            void Choose(bool youFirst) { if (chosen) return; chosen = true; done(youFirst); }
            StartCoroutine(Toss());

            System.Collections.IEnumerator Toss()
            {
                // Up and down again, flipping end over end, landing on the winner's side (the winner's face up last).
                const float Flight = 1.3f;
                if (!FxSettings.ReducedMotion) Sounds.Play("coin_spin", 0.8f);
                if (!FxSettings.ReducedMotion)
                    for (float t = 0; t < Flight && flier != null; t += Time.unscaledDeltaTime)
                    {
                        float k = t / Flight;
                        float spin = k * 7f * Mathf.PI; // three and a half turns
                        float flip = Mathf.Cos(spin);
                        flier.localScale = new Vector3(1, Mathf.Max(0.04f, Mathf.Abs(flip)), 1) * (1f + 0.25f * Mathf.Sin(k * Mathf.PI));
                        flier.anchoredPosition = new Vector2(0, Mathf.Sin(k * Mathf.PI) * 150f);
                        // Three and a half turns end on the other face: start on the loser's so it lands on the winner's.
                        ShowFace((flip >= 0) != youWin);
                        yield return null;
                    }
                if (flier == null) yield break;
                flier.localScale = Vector3.one;
                flier.anchoredPosition = Vector2.zero;
                ShowFace(youWin);
                Sounds.Play("coin_land", 0.7f);
                Sounds.PlayLater(0.12f, youWin ? "win" : "turn");
                if (youWin)
                {
                    result.text = $"{you} {won}!";
                    detail.text = $"{First}\n{Second}" + (hint.Length > 0 ? $"\n<color={T.GoldHex}>{hint}</color>" : "");
                    // Neither side is pushed unless the tip recommends one.
                    int tipped = TipSide(hint);
                    Button(560, 700, 360, 80, "Go first", () => Choose(true), 28, tipped == 1 ? T.Primary : T.Button);
                    Button(1000, 700, 360, 80, "Go second", () => Choose(false), 28, tipped == 2 ? T.Primary : T.Button);
                    yield break;
                }
                var other = you == "You" ? "you" : you;
                result.text = $"{them} {won} and {(themFirst ? "goes first" : $"lets {other} go first")}.";
                detail.text = you != "You" ? "" : themFirst ? Second.Replace("Going second", "You go second") : First.Replace("Going first", "You go first");
                Button(760, 700, 400, 80, "Continue", () => Choose(!themFirst), 28, T.Primary);
                // On by itself after a moment, unless clicked (or another screen has replaced this one: the coin is gone).
                // It closes the screen first, as the button does: without that the duel started under the coin screen.
                yield return new WaitForSecondsRealtime(2.5f);
                if (!chosen && IsOpen && flier != null) { Close(); Choose(!themFirst); }
            }
        }

        /// <summary>The side a toss tip recommends: 1 first, 2 second, 0 neither (whichever the tip names first).</summary>
        private static int TipSide(string hint)
        {
            int At(params string[] words) => words.Select(w => hint.IndexOf(w, StringComparison.OrdinalIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            int first = At("go first", "going first"), second = At("go second", "going second");
            if (first >= 0 && (second < 0 || first < second)) return 1;
            return second >= 0 ? 2 : 0;
        }

        private static bool _coinLoaded;
        private static Texture2D? _blueFace, _redFace;

        /// <summary>
        /// The coin's faces: you.png (blue, the left side) and them.png (red) from the game's StreamingAssets/coin, or
        /// the repo's art/coin when run from the editor. Null when missing.
        /// </summary>
        private static (Texture2D? blue, Texture2D? red) CoinFaces()
        {
            if (_coinLoaded) return (_blueFace, _redFace);
            _coinLoaded = true;
            Texture2D? Load(string name)
            {
                foreach (var folder in new[]
                         {
                             System.IO.Path.Combine(Application.streamingAssetsPath, "coin"),
                             System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "..", "art", "coin")),
                         })
                {
                    var path = System.IO.Path.Combine(folder, name + ".png");
                    if (!System.IO.File.Exists(path)) continue;
                    var tex = new Texture2D(2, 2) { wrapMode = TextureWrapMode.Clamp };
                    if (tex.LoadImage(System.IO.File.ReadAllBytes(path))) return tex;
                }
                return null;
            }
            _blueFace = Load("you");
            _redFace = Load("them");
            return (_blueFace, _redFace);
        }

        // ---------------------------------------------------------------- Free Duel

        /// <summary>
        /// Free Duel setup, versus style: each side's duelist and deck with arrows to change them (the left side can be
        /// you), then Duel! (or Watch, when two characters play), a random opponent, or Back.
        /// </summary>
        public void ShowFreeDuel(Side left, Side right, bool youPlay, Action<int, int> character, Action<int, int> deck, Action random,
            Action start, Action back)
        {
            var root = Fresh();
            var T = UiTheme.Current;
            Ui.Image(Ui.Box(root, 0, 0, 1920, 120), T.Base);
            Typography.Strong(Ui.Text(Ui.Box(root, 0, 18, 1920, 50), "FREE DUEL", TextRole.Title, T.Sign));
            Ui.Text(Ui.Box(root, 0, 68, 1920, 34), "Pick who plays and with which deck. The left side can be you, or a character to watch.", TextRole.Label,
                T.TextMuted);
            DrawSide(left, 80, Ui.Hex("#4C8DFF"), arrows: true); // (both sides have deck arrows here)
            DrawSide(right, 1060, Ui.Hex("#FF5A5A"), arrows: true);
            var vs = Ui.Text(Ui.Box(root, 860, 360, 200, 160), "VS", TextRole.Display, T.Text);
            Typography.Strong(vs);
            vs.gameObject.AddComponent<Outline>().effectColor = T.Scrim.WithAlpha(0.8f);
            // Arrows for the duelist (beside the name) and the deck (beside the deck line); these redraw, not close.
            for (int seat = 0; seat < 2; seat++)
            {
                float x = seat == 0 ? 80 : 1060;
                int s = seat;
                Arrow(x + 620, 186, "<", () => character(s, -1));
                Arrow(x + 700, 186, ">", () => character(s, +1));
                Arrow(x + DeckArrowAt, 650, "<", () => deck(s, -1));
                Arrow(x + DeckArrowAt + 80, 650, ">", () => deck(s, +1));
            }
            Button(760, 940, 400, 80, youPlay ? "Duel!" : "Watch", start, 32, T.Primary);
            Button(1200, 950, 300, 60, "Random opponent", random, 20);
            Button(80, 960, 180, 56, "Back", back, 20);
        }

        private void Arrow(float x, float y, string label, Action click)
        {
            var T = UiTheme.Current;
            // Level with the line it steps (the deck line at 650 is 36 tall), and ending above the detail under it (at 690):
            // a 52-tall arrow ran down over the detail's first line ("last picked on a" under the "<").
            var b = Ui.BoxButton(_root, x, y - ArrowRise, 64, ArrowHeight, label, click, TextRole.Heading);
            b.targetGraphic.color = T.Button;
            Buttons[$"{label}@{x}:{y}"] = click;
            _arrowRects.Add((RectTransform)b.transform);
        }

        /// <summary>One side of the versus screen; <paramref name="arrows"/>: the deck arrows sit at the end of its deck line.</summary>
        private void DrawSide(Side side, float x, Color accent, bool arrows = false)
        {
            var T = UiTheme.Current;
            var panel = Ui.Box(_root, x, 160, 780, 740);
            Ui.Image(panel, T.PanelDeep);
            Ui.Image(Ui.Box(panel, 0, 0, 780, 6), accent);
            var name = Ui.Text(Ui.Box(panel, 30, 26, 720, 54), side.Name, TextRole.Title, T.Text, TextAnchor.MiddleLeft);
            Typography.Strong(name);
            if (side.Title.Length > 0)
                Typography.Italic(Ui.Text(Ui.Box(panel, 30, 80, 720, 32), side.Title, TextRole.Label, T.TextSoft, TextAnchor.MiddleLeft));

            // The duelists' portraits with two of their cards beside them; without portraits, three cards.
            var portraits = side.Portraits.Where(YgoCampaign.Game.Portraits.Has).ToList();
            int cardCount = portraits.Count > 0 ? 2 : 3;
            float cardX = 30, cardW = 230, cardH = 336, cardY = 134, gap = 250;
            if (portraits.Count > 0)
            {
                float each = portraits.Count == 1 ? 420 : 250;
                for (int i = 0; i < portraits.Count; i++)
                    YgoCampaign.Game.Portraits.Place(Ui.Box(panel, 20 + i * 180, 112, each, 368), portraits[i], mirror: x > 900);
                cardX = portraits.Count == 1 ? 460 : 470;
                cardW = 140; cardH = 204; cardY = 200; gap = 152;
            }
            if (side.ShowcaseLabels.Count == 0 && side.ShowcaseLabel.Length > 0 && side.Showcase.Count > 0)
            {
                // A sign over the cards (neon is for signs): what they're showing off.
                var label = Ui.Text(Ui.Box(panel, cardX, cardY - 26, 3 * gap, 24), side.ShowcaseLabel, TextRole.Small, T.Sign, TextAnchor.LowerLeft);
                Typography.Strong(label);
            }
            for (int i = 0; i < side.Showcase.Count && i < cardCount; i++)
            {
                // A sign over this card (No Limits: UNBANNED over a restored card, CONTRABAND over a pick).
                if (i < side.ShowcaseLabels.Count && side.ShowcaseLabels[i].Length > 0)
                {
                    var over = Ui.Text(Ui.Box(panel, cardX + i * gap, cardY - 26, cardW, 24), side.ShowcaseLabels[i], TextRole.Small, T.Sign, TextAnchor.LowerLeft);
                    Typography.Strong(over);
                    over.resizeTextForBestFit = true;
                    over.resizeTextMinSize = UiTheme.Floor;
                    over.resizeTextMaxSize = UiTheme.Size(TextRole.Small);
                }
                var slot = Ui.Box(panel, cardX + i * gap, cardY, cardW, cardH);
                Ui.Image(slot, T.Well);
                // The picture, or the plain card (foiled as a picture is).
                var image = Ui.Stretched("Art", slot, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                image.raycastTarget = false;
                var info = _cards.Get(side.Showcase[i]);
                CardFace.Draw(image, side.Showcase[i], info, new Vector2(cardW, cardH));
                CardFoil.Apply(image, info, 1f);
            }

            // (Well short of the deck arrows, shrinking to fit: a tag team's two deck names are long, and a line that ran to
            // the arrows read as running into the "<".)
            float deckWidth = arrows ? DeckLineWithArrows : 720;
            var deck = Ui.Text(Ui.Box(panel, 30, 490, deckWidth, 36), $"Deck: <b>{side.Deck}</b>{(side.DeckNote.Length > 0 ? $"  <color={T.TextDimHex}>{side.DeckNote}</color>" : "")}",
                TextRole.Subhead, T.Text, TextAnchor.MiddleLeft);
            deck.supportRichText = true;
            deck.resizeTextForBestFit = true;
            deck.resizeTextMinSize = UiTheme.Floor;
            deck.resizeTextMaxSize = UiTheme.Size(TextRole.Subhead);
            // (For the checks: the first side drawn with arrows, yours.)
            if (arrows && _arrowsDeckLine == null) { _arrowsDeckLine = deck; _arrowsDeckRight = x + 30 + deckWidth; _arrowsDeckGap = x + DeckArrowAt - _arrowsDeckRight; }
            var detail = Ui.Text(Ui.Box(panel, 30, 528, 720, 72), side.Detail, TextRole.Body, T.TextMuted, TextAnchor.UpperLeft);
            if (arrows && _youDetail == null) _youDetail = detail;
            detail.supportRichText = true;
            // Three lines of Body don't fit the 60 under the deck line: a long detail (seed, wallet, ban list) shrinks to the floor.
            detail.resizeTextForBestFit = true;
            detail.resizeTextMinSize = UiTheme.Floor;
            detail.resizeTextMaxSize = detail.fontSize;
            if (side.Quote.Length > 0)
            {
                // (Clear of the arrows' hint line under it: a long quote shrinks instead.)
                var quote = Ui.Text(Ui.Box(panel, 30, 602, 720, 74), $"“{side.Quote}”", TextRole.Heading, T.Quote, TextAnchor.MiddleLeft);
                Typography.Italic(quote);
                quote.resizeTextForBestFit = true;
                quote.resizeTextMinSize = 18;
                quote.resizeTextMaxSize = UiTheme.Size(TextRole.Heading);
            }
            if (side.Chip.Length > 0)
            {
                // The stake chip along the bottom of the side: a well with a gold edge.
                var chip = Ui.Box(panel, 30, 684, 720, 42);
                Ui.Image(chip, T.Well);
                Ui.Image(Ui.Box(chip, 0, 0, 4, 42), T.Highlight).raycastTarget = false;
                var text = Ui.Text(Ui.Box(chip, 16, 8, 696, 26), side.Chip, TextRole.Body, T.Text, TextAnchor.MiddleLeft); // one line: it shrinks rather than wraps
                text.supportRichText = true;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = UiTheme.Floor;
                text.resizeTextMaxSize = UiTheme.Size(TextRole.Body);
            }
        }

        // ---------------------------------------------------------------- Guests

        /// <summary>A special guest as the guests screen lists them.</summary>
        public sealed class GuestRow
        {
            public string Id = "", Name = "", Blurb = "", Decks = "", Status = "";
            /// <summary>Unlocked for one on one; TagLocked: its tag duel waits for a milestone (the button shows dimmed).</summary>
            public bool Unlocked, TagLocked;
            /// <summary>Whether they have a tag duel at all (a partner): without one there's no Tag button and no "Tag:&lt;id&gt;".</summary>
            public bool HasTag = true;
        }

        /// <summary>The guests screen's rows as laid out, for the checks: each row's top and bottom (1920 x 1080 layout).</summary>
        public IReadOnlyList<(float top, float bottom)> GuestRowBoxes => _guestRowBoxes;
        private readonly List<(float top, float bottom)> _guestRowBoxes = new List<(float, float)>();
        /// <summary>The guests screen's Back button's top (1920 x 1080 layout).</summary>
        public const float GuestsBackTop = 960;

        /// <summary>
        /// The special guests: who they are, what they play, and a Duel and a Tag duel button once unlocked (else the
        /// milestone that unlocks them). Buttons are also keyed "Duel:&lt;id&gt;" and "Tag:&lt;id&gt;" for checks (no Tag for a guest
        /// without a partner). Up to four rows take the full height; five a shorter step; six or more a denser one still (rows
        /// from 128 at a step of 136: six end at 936), so the last clears Back at 1920x1080 and, scaled, at 1280x720.
        /// </summary>
        public void ShowGuests(IReadOnlyList<GuestRow> guests, Action<int> duel, Action<int> tag, Action back, Action<int>? scout = null)
        {
            var root = Fresh();
            var T = UiTheme.Current;
            Ui.Image(Ui.Box(root, 0, 0, 1920, 120), T.Base);
            Typography.Strong(Ui.Text(Ui.Box(root, 0, 18, 1920, 50), "SPECIAL GUESTS", TextRole.Title, T.Sign));
            Ui.Text(Ui.Box(root, 0, 68, 1920, 34), "Duelists outside the circuits. Take them on alone or in a tag duel; beat one and they can partner you in the Tag Cups.",
                TextRole.Label, T.TextMuted);
            // Four rows at full height; five at a shorter step (rows 150, 310 ... 790-940), six or more denser (128, 264 ...
            // 808-936 for six), each clear of Back at 960.
            bool compact = guests.Count > 4, dense = guests.Count > 5;
            LastGuestRows = guests.ToList();
            float first = dense ? 128 : 150, step = dense ? 136 : compact ? 160 : 200, height = dense ? 128 : compact ? 150 : 180;
            float faceW = dense ? 100 : compact ? 118 : 140, textX = dense ? 148 : compact ? 168 : 190;
            _guestRowBoxes.Clear();
            for (int i = 0; i < guests.Count; i++)
            {
                var g = guests[i];
                float top = first + i * step;
                _guestRowBoxes.Add((top, top + height));
                var panel = Ui.Box(root, 120, top, 1680, height);
                Ui.Image(panel, T.PanelDeep);
                Ui.Image(Ui.Box(panel, 0, 0, 8, height), g.Unlocked ? T.Brass : T.Locked);
                var face = Ui.Box(panel, 24, dense ? 8 : 10, faceW, height - (dense ? 16 : 20));
                Ui.Image(face, T.Well);
                if (!g.Unlocked) Ui.Text(face, "?", TextRole.Display, T.Text.WithAlpha(0.35f));
                else if (!YgoCampaign.Game.Portraits.Place(face, g.Id))
                {
                    // Unlocked but no portrait: a gold initial on a framed tile, so it doesn't read as a locked slot.
                    float faceH = height - (dense ? 16 : 20);
                    Ui.Image(Ui.Box(face, 0, 0, faceW, faceH), T.Brass);
                    var tile = Ui.Box(face, 2, 2, faceW - 4, faceH - 4);
                    Ui.Image(tile, T.Placeholder);
                    Typography.Strong(Ui.Text(tile, g.Name.Substring(0, 1), TextRole.Display, T.Gold));
                }
                // The name, the blurb, then the decks and status lines (moved up in a compact row; in a dense one the blurb is one line).
                var name = Ui.Text(Ui.Box(panel, textX, dense ? 4 : compact ? 8 : 14, 1100, dense ? 36 : compact ? 40 : 46), g.Unlocked ? g.Name : "???",
                    UiTheme.Mapped(dense ? 26 : compact ? 28 : 30), g.Unlocked ? T.Text : T.Text.WithAlpha(0.5f), TextAnchor.MiddleLeft);
                Typography.Strong(name);
                Ui.Text(Ui.Box(panel, textX, dense ? 40 : compact ? 50 : 60, 1140, dense ? 26 : compact ? 42 : 50), g.Blurb, TextRole.Body, T.TextSoft,
                    dense ? TextAnchor.MiddleLeft : TextAnchor.UpperLeft);
                if (g.Unlocked) Ui.Text(Ui.Box(panel, textX, dense ? 66 : compact ? 90 : 108, 1140, dense ? 24 : compact ? 26 : 28), g.Decks, TextRole.Small, T.TextQuiet, TextAnchor.MiddleLeft).supportRichText = true;
                Ui.Text(Ui.Box(panel, textX, dense ? 92 : compact ? 116 : 138, 1140, dense ? 28 : compact ? 28 : 30), g.Status, TextRole.Body, g.Unlocked ? T.Good : T.TextQuiet, TextAnchor.MiddleLeft)
                    .supportRichText = true;
                if (!g.Unlocked) continue;
                int index = i;
                Action fight = () => { Close(); duel(index); }, team = () => { Close(); tag(index); };
                float buttonY = dense ? 12 : compact ? 18 : 36, buttonH = dense ? 50 : compact ? 54 : 60;
                Ui.BoxButton(panel, 1330, buttonY, 150, buttonH, "Duel", fight, TextRole.Subhead).targetGraphic.color = T.Primary;
                Buttons[$"Duel:{g.Id}"] = fight;
                if (g.HasTag)
                {
                    // A locked tag duel stays clickable (it says what opens it) but reads as locked.
                    var tagButton = Ui.BoxButton(panel, 1500, buttonY, 160, buttonH, g.TagLocked ? "Tag locked" : "Tag duel", team, TextRole.Subhead);
                    tagButton.targetGraphic.color = g.TagLocked ? T.ButtonOff : T.Button;
                    if (g.TagLocked) tagButton.GetComponentInChildren<Text>().color = T.Text.WithAlpha(0.45f);
                    Buttons[$"Tag:{g.Id}"] = team;
                }
                if (scout != null)
                {
                    Action look = () => scout(index);
                    Ui.BoxButton(panel, 1330, dense ? 72 : compact ? 88 : 112, 330, dense ? 40 : compact ? 42 : 44, g.HasTag ? "Scout their decks" : "Scout the next deck", look, TextRole.Label).targetGraphic.color = T.Button;
                    Buttons[$"Scout:{g.Id}"] = look;
                }
            }
            Button(80, GuestsBackTop, 180, 56, "Back", back, 20);
        }

        // ---------------------------------------------------------------- Lessons

        /// <summary>Gus's chain lessons: each with what it teaches, a card, and whether you've passed it.</summary>
        public void ShowLessons(IReadOnlyList<ChainLesson> lessons, Func<ChainLesson, bool> passed, Action<int> start, Action back)
        {
            var root = Fresh();
            var T = UiTheme.Current;
            Ui.Image(Ui.Box(root, 0, 0, 1920, 120), T.Base);
            Typography.Strong(Ui.Text(Ui.Box(root, 0, 18, 1920, 50), "GUS'S LESSONS", TextRole.Title, T.Sign));
            Ui.Text(Ui.Box(root, 0, 68, 1920, 34), "Short duels from a set position. Gus plays only what each lesson needs.", TextRole.Label, T.TextMuted);
            if (YgoCampaign.Game.Portraits.Has("old-gus")) YgoCampaign.Game.Portraits.Place(Ui.Box(root, 60, 200, 520, 700), "old-gus");
            // The guided first duel (if listed) comes first and isn't numbered; the chain lessons are 1, 2, 3.
            int numbered = 0;
            const float top = 146, step = 200, height = 182;
            // One primary: the first lesson not passed yet.
            int nextUp = -1;
            for (int i = 0; i < lessons.Count && nextUp < 0; i++) if (!passed(lessons[i])) nextUp = i;
            for (int i = 0; i < lessons.Count; i++)
            {
                var lesson = lessons[i];
                bool first = lesson == ChainLessons.FirstDuel;
                if (!first) numbered++;
                bool done = passed(lesson);
                var panel = Ui.Box(root, 640, top + i * step, 1200, height);
                Ui.Image(panel, T.PanelDeep);
                Ui.Image(Ui.Box(panel, 0, 0, 8, height), done ? T.GoodStripe : T.Brass);
                var slot = Ui.Box(panel, 30, 14, 106, 154);
                Ui.Image(slot, T.Well);
                if (lesson.Showcase != 0)
                {
                    // The picture, or the plain card.
                    var image = Ui.Stretched("Art", slot, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                    image.raycastTarget = false;
                    CardFace.Draw(image, lesson.Showcase, _cards.Get(lesson.Showcase), new Vector2(106, 154));
                }
                var title = Ui.Text(Ui.Box(panel, 166, 16, 780, 46), first ? lesson.Title : $"{numbered}.  {lesson.Title}", TextRole.Title, T.Text, TextAnchor.MiddleLeft);
                Typography.Strong(title);
                Ui.Text(Ui.Box(panel, 166, 66, 780, 60), lesson.Teaches, TextRole.Subhead, T.TextSoft, TextAnchor.UpperLeft);
                // The mark says it in words too, not only in green.
                Ui.Text(Ui.Box(panel, 166, 130, 500, 36), done ? "✓  Passed" : "Not passed yet", TextRole.Label, done ? T.Good : T.TextQuiet, TextAnchor.MiddleLeft);
                int index = i;
                Button(640 + 960, top + i * step + 55, 200, 72, first ? "First duel" : $"Lesson {numbered}", () => start(index), 22, i == nextUp ? T.Primary : T.Button);
            }
            Button(80, 960, 180, 56, "Back", back, 20);
        }

        /// <summary>
        /// A new campaign, after the opening scene: Gus offers his guided first duel, or straight on to the circuits.
        /// </summary>
        public void ShowFirstDuelOffer(Action practice, Action skip)
        {
            var root = Fresh();
            var T = UiTheme.Current;
            if (YgoCampaign.Game.Portraits.Has("old-gus")) YgoCampaign.Game.Portraits.Place(Ui.Box(root, 120, 180, 560, 760), "old-gus");
            var head = Ui.Text(Ui.Box(root, 740, 196, 1100, 78), "Before your first circuit", TextRole.Display, T.Gold, TextAnchor.MiddleLeft);
            Typography.Strong(head);
            var quote = Ui.Text(Ui.Box(root, 740, 300, 1060, 120),
                "Gus: “New to dueling, or just new to the shop? I can walk you through a practice duel first: the board, the phases, and how to answer a card. Three minutes, tops.”",
                TextRole.Heading, T.Quote, TextAnchor.UpperLeft);
            Typography.Italic(quote);
            Ui.Text(Ui.Box(root, 740, 430, 1060, 40), "You can play it any time from Lessons, at the top of the campaign screen.", TextRole.Subhead, T.TextMuted, TextAnchor.MiddleLeft);
            Button(740, 510, 320, 76, "Practice duel", practice, 26, T.Primary);
            Button(1090, 510, 380, 76, "Skip: I know how to play", skip, 22);
        }

        /// <summary>After a lesson: Gus's verdict and what to do next (<paramref name="next"/>: the next lesson, or where the first duel leads).</summary>
        public void ShowLessonResult(ChainLesson lesson, bool passed, string detail, Action again, Action? next, Action back, string nextLabel = "Next lesson")
        {
            var root = Fresh();
            var T = UiTheme.Current;
            if (YgoCampaign.Game.Portraits.Has("old-gus")) YgoCampaign.Game.Portraits.Place(Ui.Box(root, 120, 180, 560, 760), "old-gus");
            var head = Ui.Text(Ui.Box(root, 740, 220, 1100, 90), passed ? "Lesson passed!" : "Not quite", TextRole.Display, passed ? T.Good : T.BadWarm, TextAnchor.MiddleLeft);
            Typography.Strong(head);
            Ui.Text(Ui.Box(root, 740, 316, 1100, 44), lesson.Title, TextRole.Heading, T.Gold, TextAnchor.MiddleLeft);
            Ui.Text(Ui.Box(root, 740, 380, 1060, 120), detail, UiTheme.Size(TextRole.Heading), T.TextBody, TextAnchor.UpperLeft); // a paragraph: the body face
            if (passed)
            {
                var quote = Ui.Text(Ui.Box(root, 740, 520, 1060, 160), $"Gus: “{lesson.PassLine}”", TextRole.Heading, T.Quote, TextAnchor.UpperLeft);
                Typography.Italic(quote);
            }
            if (next != null && passed)
            {
                Button(740, 760, 300, 76, nextLabel, next, 26, T.Primary);
                Button(1070, 760, 260, 76, "Play it again", again, 24);
            }
            else
            {
                // Not passed: trying again comes first; a way on (the first duel's skip) beside it.
                Button(740, 760, 260, 76, passed ? "Play it again" : "Try again", again, 24, passed ? T.Button : T.Primary);
                if (next != null) Button(1030, 760, 300, 76, nextLabel, next, 24);
            }
            Button(80, 960, 180, 56, "Lessons", back, 20);
        }

        /// <summary>Up to three cards that show a deck off: its starters first, then its strongest monsters, then anything.</summary>
        public static List<uint> ShowcaseOf(Deck deck, ICardDataProvider cards)
        {
            var picks = new List<uint>();
            void Take(IEnumerable<uint> codes)
            {
                foreach (var code in codes)
                    if (picks.Count < 3 && cards.Get(code) != null && !picks.Contains(code)) picks.Add(code);
            }
            Take(deck.Starters);
            Take(deck.Extra.Concat(deck.Main).Where(c => cards.Get(c) is CardInfo i && (i.Type & CardType.Monster) != 0)
                .OrderByDescending(c => cards.Get(c)!.Attack));
            Take(deck.Main);
            return picks;
        }

        // ---------------------------------------------------------------- Results

        /// <summary>DP counting in, then a fanfare for a level-up, a sparkle for tokens, a chime for anything unlocked.</summary>
        private System.Collections.IEnumerator ResultSounds(Results r)
        {
            yield return new WaitForSecondsRealtime(0.4f);
            if (r.Earnings.Count > 0) Sounds.Play("dp");
            if (r.Unlocks.Count == 0) yield break;
            yield return new WaitForSecondsRealtime(0.5f);
            if (r.Unlocks.Any(u => u.StartsWith("Level"))) Sounds.Play("level_up");
            else Sounds.Play("rare", 0.7f);
            if (r.Unlocks.Any(u => u.Contains("Card Token")))
            {
                yield return new WaitForSecondsRealtime(0.9f);
                Sounds.Play("token");
            }
        }

        /// <summary>
        /// "Level 2! +1 Card Token: ..." and "Level 3!" as one row ("Level 3! (2 levels up) +1 Card Token: ..."). Unlocks in
        /// another shape stay as they are.
        /// </summary>
        private static List<string> MergeLevelUps(List<string> unlocks)
        {
            var level = new System.Text.RegularExpressions.Regex(@"^Level (\d+)!(?: \+(\d+) Card Tokens?(:.*)?)?$");
            var ups = unlocks.Select(u => level.Match(u)).Where(m => m.Success).ToList();
            if (ups.Count < 2) return unlocks;
            int top = ups.Max(m => int.Parse(m.Groups[1].Value));
            int tokens = ups.Sum(m => m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0);
            bool trader = ups.Any(m => m.Groups[3].Success);
            var merged = $"Level {top}! ({ups.Count} levels up)"
                         + (tokens > 0 ? $" +{tokens} Card Token{(tokens > 1 ? "s" : "")}" + (trader ? $": trade {(tokens > 1 ? "them" : "it")} for any card at the Card Trader" : "") : "");
            var list = new List<string>();
            bool placed = false;
            foreach (var u in unlocks)
            {
                if (!level.IsMatch(u)) list.Add(u);
                else if (!placed) { list.Add(merged); placed = true; }
            }
            return list;
        }

        /// <param name="buttons">
        /// Buttons in place of Continue / Rematch / Card Shop (a bracket's result: "Skip to the end" and "Follow the
        /// bracket"); the first is the primary.
        /// </param>
        public void ShowResults(Results r, Action continueAction, Action rematch, Action shop, IReadOnlyList<(string label, Action act)>? buttons = null)
        {
            var root = Fresh();
            var T = UiTheme.Current;
            var headline = Ui.Text(Ui.Box(root, 0, 60, 1920, 110), r.Headline, TextRole.Display, r.Won ? T.Gold : T.Text);
            Typography.Strong(headline);
            headline.gameObject.AddComponent<Outline>().effectColor = T.Scrim.WithAlpha(0.8f);
            Ui.Text(Ui.Box(root, 0, 170, 1920, 34), r.Circuit, TextRole.Label, T.TextMuted);
            if (r.Quote.Length > 0)
            {
                var quote = Ui.Text(Ui.Box(root, 260, 220, 1400, 70), $"{r.Speaker}: “{r.Quote}”", TextRole.Heading, T.Quote);
                Typography.Italic(quote);
            }

            // Left: what you earned.
            var earned = Ui.Box(root, 260, 320, 680, 420);
            Ui.Image(earned, T.PanelDeep);
            Ui.Text(Ui.Box(earned, 30, 20, 620, 30), r.Stakes.Count > 0 ? "STAKES" : "DUEL POINTS", TextRole.Small, T.Caption, TextAnchor.MiddleLeft);
            for (int i = 0; i < r.Stakes.Count; i++)
            {
                Ui.Text(Ui.Box(earned, 30, 64 + i * 64, 620, 26), r.Stakes[i].what, TextRole.Body, T.TextMuted, TextAnchor.MiddleLeft);
                var value = Ui.Text(Ui.Box(earned, 30, 88 + i * 64, 620, 34), r.Stakes[i].value, TextRole.Subhead, r.Stakes[i].color, TextAnchor.MiddleLeft);
                value.resizeTextForBestFit = true;
                value.resizeTextMinSize = 14;
                value.resizeTextMaxSize = UiTheme.Size(TextRole.Subhead);
            }
            for (int i = 0; i < r.Earnings.Count; i++)
            {
                Ui.Text(Ui.Box(earned, 30, 64 + i * 44, 460, 40), r.Earnings[i].what, TextRole.Subhead, T.Text, TextAnchor.MiddleLeft);
                Ui.Text(Ui.Box(earned, 470, 64 + i * 44, 180, 40), $"+{r.Earnings[i].dp:N0}", TextRole.Heading, T.Gold, TextAnchor.MiddleRight);
            }
            Ui.Image(Ui.Box(earned, 30, 330, 620, 2), T.Divider);
            Ui.Text(Ui.Box(earned, 30, 344, 400, 50), "You have", TextRole.Subhead, T.TextMuted, TextAnchor.MiddleLeft);
            Typography.Hero(Typography.Counter(Typography.Strong(Ui.Text(Ui.Box(earned, 330, 344, 320, 50), $"{r.TotalDp:N0} DP", 30, T.Gold, TextAnchor.MiddleRight))));

            // Right: circuit progress and your record against them.
            var progress = Ui.Box(root, 980, 320, 680, 420);
            Ui.Image(progress, T.PanelDeep);
            // No match chips outside a circuit (guest duels): just the record, under its own header.
            bool chips = r.Progress.Count > 0;
            Ui.Text(Ui.Box(progress, 30, 20, 620, 30), chips ? r.ProgressTitle : "RECORD", TextRole.Small, T.Caption, TextAnchor.MiddleLeft);
            // Four chips fill the panel; more (the final's six) share its width.
            float chipStep = Math.Min(150, 640f / Math.Max(1, r.Progress.Count));
            for (int i = 0; i < r.Progress.Count; i++)
            {
                var pip = Ui.Box(progress, 30 + i * chipStep, 70, chipStep - 16, 90);
                Ui.Image(pip, r.Progress[i] ? T.PipOn : T.Inset);
                Ui.Text(pip, r.Progress[i] ? $"{r.ProgressItem} {i + 1}\n<b>WON</b>" : $"{r.ProgressItem} {i + 1}\n-", chipStep < 150 ? TextRole.Body : TextRole.Label,
                    r.Progress[i] ? T.Good : T.TextFaint).supportRichText = true;
            }
            Ui.Text(Ui.Box(progress, 30, chips ? 190 : 64, 620, 80), r.Record, TextRole.Subhead, T.Text, TextAnchor.UpperLeft).supportRichText = true;
            // EXP bar.
            Typography.Counter(Ui.Text(Ui.Box(progress, 30, 290, 620, 30), $"LEVEL {r.Exp.level}   <color={T.GoodHex}>+{r.Exp.earned} EXP</color>", TextRole.Body, T.Text, TextAnchor.MiddleLeft))
                .supportRichText = true;
            var bar = Ui.Box(progress, 30, 326, 620, 22);
            Ui.Image(bar, T.Well);
            // At the cap the bar is full and says so (EXP keeps counting past the last level: no "to level 100").
            bool maxed = r.Exp.level >= Leveling.MaxLevel;
            float fill = maxed ? 1 : r.Exp.needed > 0 ? Mathf.Clamp01((float)r.Exp.into / r.Exp.needed) : 0;
            Ui.Image(Ui.Box(bar, 0, 0, 620 * fill, 22), T.Meter);
            ExpLine = ExpToNext(r.Exp.level, r.Exp.into, r.Exp.needed);
            Ui.Text(Ui.Box(progress, 30, 354, 620, 26), ExpLine, TextRole.Small, T.TextMuted, TextAnchor.MiddleLeft);

            // Unlocks: the big news, in gold. Level-ups share one row; the rows get compact when many, so the buttons stay
            // at least 60 px above the bottom edge.
            var unlocks = MergeLevelUps(r.Unlocks);
            // Rows have a 4 px gap each; the buttons sit 20 px under the last one.
            const float rowsTop = 756, buttonsTop = 1080 - 60 - 70, gap = 16;
            float step = unlocks.Count == 0 ? 46 : Mathf.Clamp((buttonsTop - gap - rowsTop) / unlocks.Count, 26, 46);
            for (int i = 0; i < unlocks.Count; i++)
            {
                var row = Ui.Box(root, 260, rowsTop + i * step, 1400, step - 4);
                Ui.Image(row, T.Unlock);
                var text = Ui.Text(row, unlocks[i], step >= 46 ? TextRole.Subhead : step >= 34 ? TextRole.Body : TextRole.Small, T.GoldSoft);
                Typography.Strong(text);
            }

            StartCoroutine(ResultSounds(r));
            float y = Math.Max(900, rowsTop + gap + unlocks.Count * step);
            if (buttons != null && buttons.Count > 0)
            {
                const float width = 300, between = 20;
                float x = (1920 - (buttons.Count * width + (buttons.Count - 1) * between)) / 2;
                for (int i = 0; i < buttons.Count; i++)
                    Button(x + i * (width + between), y, width, 70, buttons[i].label, buttons[i].act, i == 0 ? 26 : 22, i == 0 ? T.Primary : T.Button);
                return;
            }
            Button(560, y, 280, 70, "Continue", continueAction, 26, T.Primary);
            Button(860, y, 240, 70, "Rematch", rematch, 22);
            Button(1120, y, 240, 70, "Card Shop", shop, 22);
        }
    }
}
