using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Back Alley's district on the campaign screen (docs/dev/ALLEY-SPEC-2026-11-01.md section 5, Phases A and B): the third
    /// tab (Circuits | Library | Back Alley) once the alley is open, the whole screen rebuilt in the BackAlley theme while
    /// it's shown (a 300 ms crossfade; instant under Reduced motion; the rain bed plays under it), the alley map on the left
    /// (brick, chain-link and tape behind its tables, Moth's van and Baz's chalkboard, each locked until it opens) and a
    /// table's panel on the right: tonight's four seats in order, who they are, the card each puts up and its value, one red
    /// Duel for the next unbeaten seat, the Encore once its headliners are beaten, and your deck under the Alley's rules.
    /// Orvel's Table has its own panel (one seat, both stakes: your DP and a spare card of yours). The van node opens Under
    /// the Counter (<see cref="VanScreen"/>). Baz's chalkboard (Phase C) holds the alley's brackets: the Alley Brawl once
    /// Dumpster Row is cleared, the Midnight Open once The Black Market is, each with its entry, its pay and your record, and
    /// Enter (the entry dialog, <see cref="BracketScreen.ShowAlleyEntry"/>) or the running bracket. Nyx's Table is a table panel
    /// whose seats are the house's but the first. On the Library's shelf, the Unlisted binder keeps the contraband decks
    /// you've beaten, a page each.
    /// </summary>
    public sealed partial class CampaignScreen
    {
        private enum District { Circuits, Library, Alley }

        /// <summary>The theme the screen was last built in (it's rebuilt whole when that changes).</summary>
        private UiTheme? _builtTheme;
        private bool _alleyView;
        private int _selectedAlley = -1;
        private Button _alleyTab = null!;

        /// <summary>The Back Alley is shown: the deck editor and the duel controller use the Alley's rules.</summary>
        public bool InAlley => _alleyView;

        /// <summary>The table shown on the right in the alley (-1: none).</summary>
        public int AlleyTableShown => _alleyView ? _selectedAlley : -1;

        /// <summary>The No Limits table is shown (docs/dev/NO-LIMITS-SPEC-2026-11-02.md): the deck editor from its panel uses the No Limits rules.</summary>
        public bool InNoLimits => _alleyView && _state.IsNoLimitsTable(_selectedAlley);

        /// <summary>The wall just opened on this tab show (<see cref="CampaignState.OpenWall"/>): its scene plays with the tab's.</summary>
        private bool _wallJustOpened;

        /// <summary>Duel the next seat at a table. The screen has closed itself.</summary>
        public event Action<int>? AlleyDuel;
        /// <summary>An encore: a beaten headliner of a table (its id). The screen has closed itself.</summary>
        public event Action<int, string>? AlleyEncore;
        /// <summary>Moth's van, clicked on the map. The screen has closed itself.</summary>
        public event Action? VanClicked;
        /// <summary>
        /// Baz's chalkboard: a bracket's Enter (its entry dialog over the chalkboard) or its running bracket, by kind ("brawl",
        /// "midnight"). The screen stays open under the dialog.
        /// </summary>
        public event Action<string>? ChalkboardClicked;
        /// <summary>The exhibition's Watch: its two sides' keys ("hl:&lt;headliner&gt;", "rg:&lt;table&gt;.&lt;seat&gt;"). The screen has closed itself.</summary>
        public event Action<string, string>? ExhibitionClicked;
        /// <summary>Watch the combo on a headliner's Unlisted page (its id). The screen has closed itself.</summary>
        public event Action<string>? WatchComboClicked;

        /// <summary>Remembers the alley was shown (its tab's "new" mark).</summary>
        private const string AlleySeen = "district:alley";
        /// <summary>The tab's status line (docs/dev/ALLEY-STORY-2026-11-01.md section 1).</summary>
        private const string AlleyHelp =
            "The Back Alley · one copy of anything · stake Duel Points, win their card · your own cards ride only at Orvel's Table";
        /// <summary>The status line while the No Limits table is shown (docs/dev/NO-LIMITS-STORY-2026-11-02.md section 1).</summary>
        private const string NoLimitsHelp = "No Limits · no list, three of anything · they stake the card the list took · everybody gets to play";
        /// <summary>The No Limits table's rules chip (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 4.2).</summary>
        public const string NoLimitsRulesChip = "No list · three of anything · everybody gets to play · turn 40 · higher LP wins";
        /// <summary>Seat 4's tag at No Limits (NO-LIMITS-STORY's word; never "champion" in-world).</summary>
        public const string EndChairTag = "THE END CHAIR";

        /// <summary>The tab's status line: the No Limits table's while it's shown, else the alley's.</summary>
        private string AlleyStatus => InNoLimits ? NoLimitsStatus() : AlleyHelp;

        /// <summary>
        /// The No Limits panel's line: its rules, then Baz MCing the depot (alley-voice.txt's baz:nolimits, as he does every
        /// board in the alley: docs/dev/NO-LIMITS-STORY-2026-11-02.md), a line each time the table's shown.
        /// </summary>
        private string NoLimitsStatus()
        {
            if (_state.AlleyLine("baz:nolimits", "") is not { } baz) return NoLimitsHelp;
            _state.Save(); // the line, heard
            BazLine = baz.text;
            return $"{NoLimitsHelp}  ·  {(baz.speaker.Length > 0 ? baz.speaker + ": " : "")}“{baz.text}”";
        }

        /// <summary>For checks: Baz's last line on the No Limits panel ("" before one).</summary>
        public string BazLine { get; private set; } = "";

        private bool AlleyShown(Profile profile) => profile.AlleyOpen && _state.AlleyReady;

        /// <summary>Baz's chalkboard's place on the map (and in <see cref="_selectedAlley"/>): after the tables and Moth's van.</summary>
        private int Chalkboard => _state.AlleyData.Tables.Count + 1;

        /// <summary>
        /// The chalkboard opens with its first bracket's table (the Alley Brawl: Dumpster Row cleared), or with the first
        /// headliner you meet (its exhibition, HEADLINERS "The show").
        /// </summary>
        private bool ChalkboardOpen(Profile profile)
        {
            var data = _state.AlleyData;
            return ChalkboardBrackets(profile).Any(b => data.IndexOf(b.Opens) is var t && t >= 0 && BackAlley.IsCleared(profile, data, t)) || ExhibitionOpen(profile);
        }

        /// <summary>
        /// The brackets Baz's chalkboard lists, in the data's order: all of them, except the No Limits Invitational until the wall
        /// has opened (<see cref="BackAlley.WallEvent"/> seen), so the board never spoils No Limits; from then on it shows
        /// whatever <see cref="Brackets.Opening"/> says ("Coming soon", "Clear No Limits to unlock", open).
        /// </summary>
        private List<AlleyBracketSpec> ChalkboardBrackets(Profile profile) =>
            _state.AlleyData.Brackets.Where(b => b.Kind != Brackets.InvitationalKind || profile.Seen.Contains(BackAlley.WallEvent)).ToList();

        /// <summary>Whether a pick on the map (a table, or the chalkboard) can be shown.</summary>
        private bool AlleyPickOpen(Profile profile, int pick) =>
            pick == Chalkboard ? ChalkboardOpen(profile) : pick >= 0 && pick < _state.AlleyData.Tables.Count && BackAlley.IsUnlocked(profile, _state.AlleyData, pick);

        private string HeldNote(Profile profile) =>
            profile.AnteLive is { Held: > 0 } live ? $" <size=18><color={UiTheme.Current.TextDimHex}>({live.Held:N0} held)</color></size>" : "";

        /// <summary>The switch's box: two tabs as it always was until the alley opens, then three (390 px: 104, 130, 140).</summary>
        private void LayOutDistricts(Profile profile)
        {
            bool three = AlleyShown(profile);
            var box = (RectTransform)_districts.transform;
            box.anchoredPosition = new Vector2(three ? 450 : 600, box.anchoredPosition.y);
            box.sizeDelta = new Vector2(three ? 390 : 240, box.sizeDelta.y);
            _alleyTab.gameObject.SetActive(three);
            // The map's title ends where the switch begins.
            _mapTitle.rectTransform.sizeDelta = new Vector2(three ? 400 : 800, _mapTitle.rectTransform.sizeDelta.y);
            _mapTitle.resizeTextForBestFit = three;
            _mapTitle.resizeTextMinSize = 10;
            _mapTitle.resizeTextMaxSize = 15;
            var T = UiTheme.Current;
            _alleyTab.GetComponentInChildren<Text>().text = profile.Seen.Contains(AlleySeen) ? "Back Alley" : $"Back Alley <color={T.GoodHex}>(new)</color>";
        }

        private string AlleyTitle(Profile profile)
        {
            var data = _state.AlleyData;
            int cleared = Enumerable.Range(0, data.Tables.Count).Count(t => BackAlley.IsCleared(profile, data, t));
            return $"THE BACK ALLEY  ·  {cleared}/{data.Tables.Count} tables cleared";
        }

        /// <summary>The whole screen built again in the current theme (its overlays go with it).</summary>
        private void Rebuild()
        {
            bool open = _root != null && _root.gameObject.activeSelf;
            if (_root != null)
            {
                // The overlay's canvas goes with it (Ui.Overlay puts the root under its own canvas).
                var canvas = _root.parent != null ? _root.parent.gameObject : _root.gameObject;
                canvas.SetActive(false);
                Destroy(canvas);
            }
            _locker = null;
            _bounties = null;
            _trade = null;
            _newGame = null;
            _lockerNote = null;
            _tradeNote = null;
            Build();
            _root.gameObject.SetActive(open);
        }

        /// <summary>The 300 ms crossfade into a district in another theme (instant under Reduced motion).</summary>
        private void FadeIn()
        {
            if (FxSettings.ReducedMotion || !isActiveAndEnabled) return;
            var group = _root.gameObject.GetComponent<CanvasGroup>();
            if (group == null) group = _root.gameObject.AddComponent<CanvasGroup>();
            StartCoroutine(Fade(group));
        }

        private static System.Collections.IEnumerator Fade(CanvasGroup group)
        {
            const float seconds = 0.3f;
            for (float t = 0; t < seconds && group != null; t += Time.unscaledDeltaTime)
            {
                group.alpha = t / seconds;
                yield return null;
            }
            if (group != null) group.alpha = 1;
        }

        /// <summary>The district switch: Circuits and the Library are main street; the Back Alley its own theme.</summary>
        private void ShowDistrictView(District d)
        {
            var profile = _state.Profile;
            if (profile == null) return;
            if (d == District.Alley)
            {
                if (!AlleyShown(profile) || _alleyView) return;
                _alleyView = true;
                _libraryView = false;
                var data = _state.AlleyData;
                // (The wall is checked in Open, with the tab's every show.)
                if (!AlleyPickOpen(profile, _selectedAlley)) _selectedAlley = FirstTableToPlay(profile);
                if (profile.Seen.Add(AlleySeen)) _state.Save();
                Open();
                FadeIn();
                return;
            }
            if (_alleyView)
            {
                // Back up the stairs: main street again, the whole screen with it.
                _alleyView = false;
                _libraryView = false;
                UiTheme.Current = UiTheme.MainStreet;
                Open();
                FadeIn();
                if (d == District.Library) ShowMainStreetDistrict(true);
                return;
            }
            ShowMainStreetDistrict(d == District.Library);
        }

        /// <summary>The first open table not cleared yet (the first one when all are).</summary>
        private int FirstTableToPlay(Profile profile)
        {
            var data = _state.AlleyData;
            var open = Enumerable.Range(0, data.Tables.Count).Where(t => BackAlley.IsUnlocked(profile, data, t)).ToList();
            var playing = open.Where(t => !BackAlley.IsCleared(profile, data, t)).ToList();
            return playing.Count > 0 ? playing[0] : open.Count > 0 ? open[0] : -1;
        }

        /// <summary>
        /// After every Open: a notice from a load (a duel left mid-way, Moth selling a railed card), and the alley's scenes
        /// still to see: the rumor once it's open, the first visit and the table shown while it's shown.
        /// </summary>
        private void AlleyOnOpen(string? message)
        {
            var profile = _state.Profile;
            if (profile == null) return;
            var T = UiTheme.Current;
            bool notice = _state.AlleyNotice.Length > 0;
            if (notice) _status.text = $"<color={T.BadHex}>{_state.AlleyNotice}</color>" + (message != null ? "  ·  " + message : "");
            var events = new List<string>();
            if (profile.AlleyOpen) events.Add(BackAlley.OpenEvent);
            events.AddRange(_state.AlleyNoticeEvents);
            // Ines's card: due once Orvel's Table is cleared and the goat cup's scene seen, whichever came second (the catch-up).
            if (_state.HeldCardDue) events.Add(BackAlley.HeldEvent);
            // The finale: once the post-game's strands are closed (the Library's, the alley's, No Limits' once it ships), once.
            if (_state.FinaleDue) events.Add(Story.FinaleEvent);
            // The Champion's Ladder: open (the final won and Ilse beaten once) and its opening scene not seen yet, once (CampaignState.Ladder).
            if (_state.LadderDue) events.Add(Ladder.OpenEvent);
            if (_alleyView)
            {
                events.Add(BackAlley.VisitEvent);
                // The wall opens (nolimits), next to the visit: on the tab show after Nyx's clear and the alley-closing scene, never with them.
                if (_wallJustOpened) { events.Add(BackAlley.WallEvent); _wallJustOpened = false; }
                if (_selectedAlley >= 0 && _selectedAlley < _state.AlleyData.Tables.Count) events.Add("open:" + _state.AlleyData.Tables[_selectedAlley].Name);
                // Baz's chalkboard: a title whose scene the Contraband reveal never reached (the app closed) plays it now, once.
                if (_selectedAlley == Chalkboard && ChalkboardOpen(profile)) events.AddRange(Brackets.AlleyTitleEvents(profile, _state.AlleyData));
            }
            if (events.Count > 0) StoryEvents?.Invoke(events.ToArray());
            // A scene that closed the screen to play shows the notice when the screen opens again.
            if (IsOpen)
            {
                _state.AlleyNotice = "";
                _state.ClearAlleyStory();
            }
        }

        // ---- The alley map --------------------------------------------------------------------------------------------

        private const float AlleyRow = 104, AlleyStep = 116;

        /// <summary>What a map row shows, for the checks: its name, whether it opens, and its lock line ("" when open).</summary>
        private readonly List<(string name, bool open, string locked)> _alleyNodes = new List<(string, bool, string)>();

        /// <summary>
        /// The alley as a list of places down a wet backstreet (the circuit map's list and scroll, not its art): the tables in
        /// order, then Moth's van and Baz's chalkboard; each locked until it opens. Returns the map's height.
        /// </summary>
        private float BuildAlleyMap(Profile profile)
        {
            var T = UiTheme.Current;
            var data = _state.AlleyData;
            _alleyNodes.Clear();
            var rows = new List<(string name, string sub, bool open, bool cleared, string progress, Action? click)>();
            for (int t = 0; t < data.Tables.Count; t++)
            {
                var table = data.Tables[t];
                bool open = BackAlley.IsUnlocked(profile, data, t), cleared = BackAlley.IsCleared(profile, data, t);
                // No Limits waits behind Nyx's wall (the nolimits event), not on a clear alone.
                string locked = open ? "" : !table.Ready ? "Coming soon" : table.Tier == AlleyTier.NoLimits ? "Behind Nyx's wall" : $"Clear {table.Opens}";
                string progress = "";
                if (open && table.Seats.Count == 0 && profile.AlleyTables.TryGetValue(table.Id, out var st))
                    progress = $"{Enumerable.Range(1, table.SeatCount).Count(s => (st.WonMask & (1 << (s - 1))) != 0)} / {table.SeatCount} tonight";
                else if (open) progress = $"0 / {table.SeatCount} tonight";
                int index = t;
                rows.Add((table.Name, open ? $"{TierName(table.Tier)} · stake {Ante.StakeFor(table.Tier):N0} DP" : locked, open, cleared, progress,
                    open ? () => SelectAlleyTable(index) : (Action?)null));
                _alleyNodes.Add((table.Name, open, locked));
            }
            // Moth's van opens with the alley; Baz's chalkboard with the Alley Brawl's table.
            int railed = profile.Rail.Count;
            rows.Add(("Moth's van", "Under the Counter · the shelf, used & graded, sell", true, false,
                railed > 0 ? $"{railed} of yours on the rail" : "", () => { Close(); VanClicked?.Invoke(); }));
            _alleyNodes.Add(("Moth's van", true, ""));
            bool board = ChalkboardOpen(profile);
            var shownBrackets = ChalkboardBrackets(profile);
            var firstBracket = shownBrackets.FirstOrDefault();
            string boardLocked = board ? "" : firstBracket != null ? $"Clear {firstBracket.Opens}" : "Coming soon";
            string boardProgress = profile.Bracket is Bracket running && Brackets.IsAlley(running)
                ? $"{running.Name}: {(running.Done ? "results" : running.RoundName(running.CurrentRound()))} ▶"
                : profile.AlleyBrackets.Values.Sum(r => r.Titles) is int titles && titles > 0 ? $"{titles} title{(titles == 1 ? "" : "s")}" : "";
            rows.Add(("Baz's chalkboard", board ? string.Join(" · ", shownBrackets.Select(b => b.Name)) + " · entry, no stakes" : boardLocked, board, false, boardProgress,
                board ? () => SelectAlleyTable(Chalkboard) : (Action?)null));
            _alleyNodes.Add(("Baz's chalkboard", board, boardLocked));

            float height = Mathf.Max(_scroll.viewport.rect.height, 24 + rows.Count * AlleyStep + 12);
            // The backdrop: a brick wall, a stretch of chain-link fence and tape (AlleyMaterials).
            AlleyMaterials.Backdrop(_circuitList, 0, 0, MapWidth, height, T.Sign);

            _rowTops.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                var (name, sub, open, cleared, progress, click) = rows[i];
                bool selected = i == _selectedAlley && (i < data.Tables.Count || i == Chalkboard);
                float y = 24 + i * AlleyStep;
                var row = Ui.Box(_circuitList, 40, y, MapWidth - 80, AlleyRow);
                Ui.Image(row, (selected ? T.RowSelected : T.Panel).WithAlpha(open ? 0.96f : 0.8f));
                Ui.Image(Ui.Box(row, 0, 0, 6, AlleyRow), selected ? T.Highlight : open ? T.Brass : T.Locked).raycastTarget = false;
                if (selected)
                {
                    var frame = Ui.Stretched("Frame", row, Vector2.zero, Vector2.one).gameObject.AddComponent<Outline>();
                    frame.effectColor = T.Highlight;
                }
                var title = Ui.Text(Ui.Box(row, 28, 12, 470, 40), name.ToUpperInvariant(), 26, open ? T.Text : T.Text.WithAlpha(0.5f), TextAnchor.MiddleLeft);
                title.fontStyle = FontStyle.Bold;
                T.Title(title);
                title.raycastTarget = false;
                var subText = Ui.Text(Ui.Box(row, 28, 54, 470, 34), sub, 16, open ? T.Gold : T.TextDim, TextAnchor.MiddleLeft);
                subText.raycastTarget = false;
                if (!open) Badge(row, "lock", new Vector2((MapWidth - 80) / 2 - 44, 0), 40, T.TextDim);
                if (cleared)
                {
                    var tick = Ui.Text(Ui.Box(row, 510, 10, 190, 40), "✓ Cleared", 20, T.Good, TextAnchor.MiddleRight);
                    tick.fontStyle = FontStyle.Bold;
                    tick.raycastTarget = false;
                }
                if (progress.Length > 0) Ui.Text(Ui.Box(row, 510, 54, 190, 34), progress, 16, T.TextSoft, TextAnchor.MiddleRight).raycastTarget = false;
                if (click != null) Ui.Clickable(row, () => { Sounds.Play("click"); click(); }, style: HoverStyle.Row);
                _rowTops.Add(y);
            }
            return height;
        }

        private static string TierName(AlleyTier tier) => tier switch
        {
            AlleyTier.Street => "Street", AlleyTier.Orvel => "Orvel's", AlleyTier.Nyx => "Nyx's", AlleyTier.NoLimits => "No Limits", _ => "Den",
        };

        private void SelectAlleyTable(int table)
        {
            if (table == _selectedAlley) return;
            _selectedAlley = table;
            Refresh();
            _status.text = AlleyStatus;
            if (table < _state.AlleyData.Tables.Count) StoryEvents?.Invoke(new[] { "open:" + _state.AlleyData.Tables[table].Name });
            // Baz's chalkboard: a title's scene its reveal never reached (the app closed during it), once.
            else if (table == Chalkboard && _state.Profile is Profile p && Brackets.AlleyTitleEvents(p, _state.AlleyData) is { Count: > 0 } due) StoryEvents?.Invoke(due.ToArray());
        }

        // ---- A table's panel ------------------------------------------------------------------------------------------

        /// <summary>A seat row as the panel shows it, for the checks.</summary>
        public sealed class AlleySeatRow
        {
            public int Seat;
            public string Handle = "", Deck = "", Stake = "";
            /// <summary>The tag after the handle ("HEADLINER", "HOST", "THE END CHAIR"...; "" for none) and No Limits' UNBANNED row ("UNBANNED: Original Sinful Spoils - Snake-Eye ×3"; "" elsewhere).</summary>
            public string Tag = "", Unbanned = "";
            public int StakeValue, Binder;
            public bool Won, Next, Met;
        }

        /// <summary>The panel's rules chip as last drawn ("Alley list · no Ban Passes · turn 40 · higher LP wins"; No Limits' own at that table).</summary>
        public string AlleyRulesLine { get; private set; } = "";

        /// <summary>The panel's seats as last drawn (for the checks).</summary>
        public List<AlleySeatRow> AlleySeats { get; } = new List<AlleySeatRow>();

        /// <summary>The panel's stake line as last drawn ("Street · stake 300 DP, held · back if you win").</summary>
        public string AlleyStakeLine { get; private set; } = "";

        private void ShowAlleyTable(int index)
        {
            var T = UiTheme.Current;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            AlleySeats.Clear();
            var profile = _state.Profile!;
            var data = _state.AlleyData;
            ChalkboardRows.Clear();
            if (index == Chalkboard && ChalkboardOpen(profile)) { ShowChalkboard(); return; }
            if (index < 0 || index >= data.Tables.Count || !BackAlley.IsUnlocked(profile, data, index))
            {
                Ui.Text(Ui.Box(_detail, 30, 20, 940, 46), "THE BACK ALLEY", 30, T.Text, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
                Ui.Text(Ui.Box(_detail, 30, 70, 940, 60), "Pick a table on the left.", 18, T.TextMuted, TextAnchor.UpperLeft);
                return;
            }
            var table = data.Tables[index];
            if (table.Tier == AlleyTier.Orvel) { ShowOrvelTable(index); return; }
            int stake = Ante.StakeFor(table.Tier);
            bool cleared = BackAlley.IsCleared(profile, data, index);
            bool noLimits = table.Tier == AlleyTier.NoLimits;
            var ctx = _state.AlleyContext!;
            var lineup = _state.AlleyLineup(index);
            int next = BackAlley.NextSeat(profile, data, index);

            PanelHeader(table.Name);
            AlleyStakeLine = $"{TierName(table.Tier)} · stake {stake:N0} DP, held · back if you win";
            Ui.Text(Ui.Box(_detail, 30, 66, 940, 28), AlleyStakeLine + (cleared ? $"  <color={T.GoodHex}>✓ Cleared</color>" : $"  ·  <color={T.GoldSoftHex}>first clear: +{AlleyEconomy.ClearTokens} Card Token</color>"),
                18, T.Gold, TextAnchor.MiddleLeft).supportRichText = true;
            // The rules, under a neon rule line (neon is for signs and rules). No Limits: no list, three of anything (section 4.2).
            AlleyRulesLine = noLimits ? NoLimitsRulesChip : "Alley list · no Ban Passes · turn 40 · higher LP wins";
            var rulesText = Ui.Text(Ui.Box(_detail, 30, 98, 940, 26), AlleyRulesLine + "  ·  Leaving a duel counts as a loss.", 15, T.TextSoft, TextAnchor.MiddleLeft);
            rulesText.resizeTextForBestFit = true;
            rulesText.resizeTextMinSize = 11;
            rulesText.resizeTextMaxSize = 15;
            Ui.Image(Ui.Box(_detail, 30, 128, 940, 2), T.Divider).raycastTarget = false;

            const float top = 144, step = 132, rowH = 122;
            for (int k = 0; k < lineup.Count; k++)
            {
                var o = lineup[k];
                var won = profile.AlleyTables.TryGetValue(table.Id, out var st) && (st.WonMask & (1 << (o.Seat - 1))) != 0;
                bool met = won || profile.LossesInARow($"alley:{table.Id}.{o.Seat}") > 0
                           || (o.IsHeadliner && profile.Headliners.TryGetValue(o.Character, out var hs) && hs.Met > 0);
                // A seat beaten tonight puts nothing more up.
                var stakeCard = won ? null : _state.AlleyStake(o);
                int binder = _state.AlleyBinderCount(o);
                // The tag after the handle: a headliner (or a wildcard), the house at Nyx's (Nyx herself the HOST); at No Limits the
                // end chair (seat 4; the code's champion is never named so in-world) and a champion wildcarding at seats 1-3.
                string tag = noLimits ? o.Seat == table.SeatCount ? EndChairTag : o.Kind == "wildcard" ? "WILDCARD" : ""
                    : o.IsHeadliner ? "HEADLINER"
                    : o.Kind == "authored" ? (table.Tier == AlleyTier.Nyx && o.Seat == table.SeatCount ? "HOST" : "HOUSE") : "";
                // No Limits' UNBANNED row: the build's first restored card and its count in the deck (section 11).
                var unbanned = noLimits ? BackAlley.Unbanned(o, ctx) : new List<uint>();
                var seat = new AlleySeatRow
                {
                    Seat = o.Seat, Handle = o.Handle, Won = won, Next = o.Seat == next, Met = met, Binder = binder, Tag = tag,
                    Deck = !met ? "???" : o.Kind == "regular" ? $"{_state.DeckTitle(o.Base)}, splashed" : _state.DeckTitle(o.Base),
                    Unbanned = unbanned.Count > 0 ? $"UNBANNED: {_state.CardName(unbanned[0])} ×{BackAlley.CopiesIn(o, unbanned[0], ctx)}" : "",
                    Stake = stakeCard != null ? _state.StakeText(stakeCard) : "", StakeValue = stakeCard != null ? _state.StakeValue(stakeCard) : 0,
                };
                AlleySeats.Add(seat);
                var row = Ui.Box(_detail, 30, top + k * step, 940, rowH);
                Ui.Image(row, won ? T.RowWon : k % 2 == 0 ? T.RowA : T.RowB);
                Silhouette(row, 12, 11, 100, o);
                var head = Ui.Text(Ui.Box(row, 128, 8, 620, 34),
                    $"Seat {o.Seat}: {o.Handle}" + (tag.Length > 0 ? $"  <color={T.VioletHex}>{tag}</color>" : "")
                    + (won ? $"  <color={T.GoodHex}>✓</color>" : ""),
                    21, T.Text, TextAnchor.MiddleLeft);
                head.fontStyle = FontStyle.Bold;
                head.supportRichText = true;
                head.resizeTextForBestFit = true;
                head.resizeTextMinSize = 15;
                head.resizeTextMaxSize = 21;
                // With an UNBANNED row the lines sit closer: deck 38, UNBANNED 58, the stake 78, the binder 100.
                bool row5 = seat.Unbanned.Length > 0;
                Ui.Text(Ui.Box(row, 128, row5 ? 38 : 42, 620, row5 ? 22 : 24), $"Deck: {seat.Deck}" + (o.Elo > 0 ? $" · Elo {o.Elo:N0}" : ""), 14, T.TextFaint, TextAnchor.MiddleLeft);
                if (row5)
                {
                    var unbannedText = Ui.Text(Ui.Box(row, 128, 58, 620, 22), seat.Unbanned, 13, T.Sign, TextAnchor.MiddleLeft);
                    unbannedText.fontStyle = FontStyle.Bold;
                    unbannedText.resizeTextForBestFit = true;
                    unbannedText.resizeTextMinSize = 10;
                    unbannedText.resizeTextMaxSize = 13;
                }
                var stakeLine = won ? $"<color={T.GoodHex}>Beaten tonight.</color>"
                    : stakeCard?.Flag == Ante.PennyFlag ? $"Puts up: <b>{seat.Stake}</b> · <color={T.TextDimHex}>Penny plays for the Common: nothing of yours is held.</color>"
                    : stakeCard != null
                    ? $"Puts up: <b>{seat.Stake}</b> · <color={T.GoldHex}>{seat.StakeValue:N0} DP</color>"
                    : $"<color={T.TextDimHex}>Binder's empty: no stakes tonight.</color>";
                var stakeText = Ui.Text(Ui.Box(row, 128, row5 ? 78 : 66, 620, row5 ? 24 : 26), stakeLine, 15, T.TextBody, TextAnchor.MiddleLeft);
                stakeText.supportRichText = true;
                stakeText.resizeTextForBestFit = true;
                stakeText.resizeTextMinSize = 12;
                stakeText.resizeTextMaxSize = 15;
                Ui.Text(Ui.Box(row, 128, row5 ? 100 : 92, 620, row5 ? 20 : 24), $"Binder: {binder} / {AlleyEconomy.BinderSize}", 13, T.TextQuiet, TextAnchor.MiddleLeft);
                if (won) Ui.Text(Ui.Box(row, 770, 0, 150, rowH), "Won\ntonight", 18, T.Good);
                else if (o.Seat == next)
                {
                    int tableIndex = index;
                    Ui.BoxButton(row, 770, (rowH - 60) / 2, 150, 60, "Duel", () => TryAlleyDuel(tableIndex), 22).targetGraphic.color = T.Primary;
                }
                else Ui.Text(Ui.Box(row, 770, 0, 150, rowH), $"After seat {o.Seat - 1}", 15, T.TextDim);
            }
            float y = top + Math.Max(1, lineup.Count) * step;
            // The Encore: once every ready headliner of the table is beaten, rematch one at the table's stake.
            if (BackAlley.EncoreOpen(profile, data, index))
            {
                float x = 30;
                foreach (var h in table.Headliners.Select(data.FindHeadliner).OfType<Headliner>().Where(h => h.Ready))
                {
                    var id = h.Id;
                    int tableIndex = index;
                    Ui.BoxButton(_detail, x, y, 260, 40, $"Encore: {h.Handle}", () => TryAlleyEncore(tableIndex, id), 16).targetGraphic.color = T.Button;
                    x += 274;
                }
                y += 52;
            }
            _state.Save(); // the binders made or refilled to show their stakes
            DrawYourDeck(y + 4, AlleyDeckProblems());
        }

        /// <summary>Your deck under the shown table's rules (the No Limits rules at No Limits; the Alley's at every other table, the chalkboard and the binder).</summary>
        private List<DeckProblem> AlleyDeckProblems() =>
            _library.Contains(_state.Profile!.Deck)
                ? _state.AlleyProblems(_library.Load(_state.Profile.Deck), _selectedAlley)
                : new List<DeckProblem> { new DeckProblem { Message = "Build a deck first (New deck)." } };

        /// <summary>
        /// A hooded figure in the duelist's tint (the code-drawn stand-in until the owner's silhouette art: a hood, the face in
        /// its shadow, shoulders); a headliner with a portrait shows the portrait.
        /// </summary>
        private void Silhouette(RectTransform row, float x, float y, float size, AlleyOpponent o)
        {
            var T = UiTheme.Current;
            var box = Ui.Box(row, x, y, size, size);
            Ui.Image(box, T.Well);
            box.gameObject.AddComponent<RectMask2D>();
            // A headliner, an authored seat (Orvel) or Penny with a portrait shows it.
            if ((o.IsHeadliner || o.Kind == "authored" || o.Character == BackAlley.PennyId) && Portraits.Place(box, o.Character)) return;
            // A nameless regular shows a grunt's face, picked by its handle (art/portraits/alley-grunt-NN.png).
            if (o.Kind == "regular" && Portraits.GruntFor(o.Handle) is string grunt && Portraits.Place(box, grunt)) return;
            var tint = new Color(((o.Tint >> 16) & 0xFF) / 255f, ((o.Tint >> 8) & 0xFF) / 255f, (o.Tint & 0xFF) / 255f);
            if (o.Tint == 0) tint = T.TextDim;
            AlleyMaterials.Silhouette(box, tint);
        }

        /// <summary>A table's panel title on its brick header band, with the tape (the title in the alley's title face).</summary>
        private void PanelHeader(string name)
        {
            var T = UiTheme.Current;
            if (T.IsAlley) AlleyMaterials.Header(_detail, 0, 0, 1000, 62, T.Sign);
            var title = Ui.Text(Ui.Box(_detail, 30, 10, 700, 48), name.ToUpperInvariant(), 32, T.Text, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            T.Title(title);
        }

        private void TryAlleyDuel(int table)
        {
            if (_state.Profile == null || BackAlley.NextSeat(_state.Profile, _state.AlleyData, table) < 0) return;
            Close();
            AlleyDuel?.Invoke(table);
        }

        private void TryAlleyEncore(int table, string headliner)
        {
            Close();
            AlleyEncore?.Invoke(table, headliner);
        }


        // ---- Baz's chalkboard (Phase C) -------------------------------------------------------------------------------

        /// <summary>A bracket's block on the chalkboard as last drawn, for the checks: its kind, its button's label, whether it opens.</summary>
        public List<(string kind, string label, bool open)> ChalkboardRows { get; } = new List<(string, string, bool)>();

        /// <summary>
        /// Baz's chalkboard: the alley's brackets (<see cref="ChalkboardBrackets"/>: the Alley Brawl, the Midnight Open, and the
        /// No Limits Invitational once the wall has opened), a block each: its size and final, the entry, the title's pay and
        /// packs (<see cref="Brackets.PackNameFor"/>) and first-title tokens (<see cref="Brackets.TokensFor"/>), where its field
        /// comes from, your record there, and Enter · entry (the entry dialog over the board), the running bracket, or why not
        /// (<see cref="Brackets.Opening"/>: "Clear The Black Market to unlock", "Coming soon"). Three blocks sit closer, so your
        /// deck (under the Alley's rules) and the exhibition stay where they were.
        /// </summary>
        private void ShowChalkboard()
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var data = _state.AlleyData;
            ChalkboardRows.Clear();
            PanelHeader("Baz's chalkboard");
            var shown = ChalkboardBrackets(profile);
            // The header as it always read; with the Invitational on the board, bracket-neutral (its packs, its rules).
            bool invitational = shown.Any(x => x.Kind == Brackets.InvitationalKind);
            AlleyStakeLine = invitational ? "Alley brackets · an entry fee, placement pay and Contraband or No Limits Packs · no stakes"
                : "Alley brackets · an entry fee, placement pay and Contraband Packs · no stakes";
            Ui.Text(Ui.Box(_detail, 30, 66, 940, 28), AlleyStakeLine, 18, T.Gold, TextAnchor.MiddleLeft);
            var rulesHeader = Ui.Text(Ui.Box(_detail, 30, 98, 940, 26), invitational
                    ? "The Alley list, or no list at the Invitational · your own decks, every round · turn 40 · higher LP wins  ·  Baz calls it, Mika streams it."
                    : "Alley list · your own decks, every round · turn 40 · higher LP wins  ·  Baz calls it, Mika streams it.", 15, T.TextSoft, TextAnchor.MiddleLeft);
            if (invitational)
            {
                rulesHeader.resizeTextForBestFit = true;
                rulesHeader.resizeTextMinSize = 11;
                rulesHeader.resizeTextMaxSize = 15;
            }
            Ui.Image(Ui.Box(_detail, 30, 128, 940, 2), T.Divider).raycastTarget = false;

            // Two blocks as they always were; three (the Invitational up) closer, in the same room.
            bool compact = shown.Count > 2;
            float top = 144, blockH = compact ? 146 : 214, step = compact ? 152 : 226;
            for (int i = 0; i < shown.Count; i++)
            {
                var spec = shown[i];
                var kind = spec.Kind;
                var record = Brackets.AlleyRecord(profile, kind);
                var blocked = Brackets.EnterBlocked(profile, data, kind);
                bool running = profile.Bracket is Bracket b && b.Kind == kind;
                // Open, "Coming soon" (its field's builds aren't all ready) or locked behind its table (Brackets.Opening).
                var opening = Brackets.Opening(profile, data, kind);
                bool unlocked = opening.Open;
                var block = Ui.Box(_detail, 30, top + i * step, 940, blockH);
                Ui.Image(block, running ? T.RowSelected : i % 2 == 0 ? T.RowA : T.RowB);
                if (running) Ui.Image(Ui.Box(block, 0, 0, 4, blockH), T.Highlight).raycastTarget = false;
                var name = Ui.Text(Ui.Box(block, 20, compact ? 4 : 10, 600, compact ? 34 : 40), spec.Name.ToUpperInvariant(), compact ? 22 : 26, unlocked ? T.Text : T.Text.WithAlpha(0.5f), TextAnchor.MiddleLeft);
                name.fontStyle = FontStyle.Bold;
                T.Title(name);
                if (record.Titles > 0)
                    Ui.Text(Ui.Box(block, 620, compact ? 4 : 10, 300, compact ? 34 : 40), $"♛ {record.Titles} title{(record.Titles == 1 ? "" : "s")}", 18, T.Gold, TextAnchor.MiddleRight).fontStyle = FontStyle.Bold;
                var pay = Brackets.PayFor(kind);
                var packs = Brackets.PacksFor(kind);
                string packName = Brackets.PackNameFor(kind);
                var size = spec.FinalBestOf > 1 ? $"{spec.Size} players · single elimination · the final best of {spec.FinalBestOf}" : $"{spec.Size} players · single elimination";
                if (kind == Brackets.InvitationalKind) size += " · no list, three of anything";
                Ui.Text(Ui.Box(block, 20, compact ? 38 : 50, 900, compact ? 20 : 24), size, compact ? 14 : 15, T.TextSoft, TextAnchor.MiddleLeft);
                // The kind's first title pays its Card Tokens once (the Midnight Open's, the Invitational's).
                int tokens = Brackets.TokensFor(kind);
                bool firstToken = tokens > 0 && Brackets.TokenKeyFor(kind) is { Length: > 0 } tokenKey && !profile.Seen.Contains(tokenKey);
                var prizes = Ui.Text(Ui.Box(block, 20, compact ? 58 : 76, 900, compact ? 22 : 26),
                    $"Entry <color={T.GoldHex}>{Brackets.EntryFor(kind):N0} DP</color> · the title +{pay[0]:N0} DP and {packs[0]} {packName}{(packs[0] == 1 ? "" : "s")}"
                    + (firstToken ? $" (+{tokens} Card Token{(tokens == 1 ? "" : "s")}, the first time)" : "")
                    + (packs.Length > 1 && packs[1] > 0 ? $" · the final +{pay[1]:N0} DP and {packs[1]} pack{(packs[1] == 1 ? "" : "s")}" : ""), 15, T.TextBody, TextAnchor.MiddleLeft);
                prizes.supportRichText = true;
                prizes.resizeTextForBestFit = true;
                prizes.resizeTextMinSize = 11;
                prizes.resizeTextMaxSize = 15;
                // Who's on the slate: the Brawl's regulars come from a cleared table (never The Black Market's); the Open's names are written.
                int brawlTable = BackAlley.BrawlTable(profile, data);
                // The Brawl's out-of-towners sit once their `after:` table is cleared (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 8).
                int outOfTowners = kind == Brackets.BrawlKind ? spec.Field.Select(AlleyBracketSpec.ParseField)
                    .Count(f => f.words.Length > 0 && f.after.Length > 0 && data.FindBuild(f.words[0]) != null && data.IndexOf(f.after) is var at && at >= 0 && BackAlley.IsCleared(profile, data, at)) : 0;
                var field = kind == Brackets.BrawlKind
                    ? outOfTowners > 0
                        ? $"The field: {outOfTowners} out-of-towners, seeded, and {7 - outOfTowners} of {(brawlTable >= 0 ? data.Tables[brawlTable].Name : "the alley")}'s regulars, splashed as at the table. You're seed 1."
                        : $"The field: seven of {(brawlTable >= 0 ? data.Tables[brawlTable].Name : "the alley")}'s regulars, splashed as at the table. You're seed 1."
                    : kind == Brackets.InvitationalKind
                        ? $"The field: the out-of-towners on their peak builds and the alley's best{(TopSeat(spec) is { Length: > 0 } top1 ? $" ({top1} seed 1)" : "")}. You're seed 2."
                        : "The field: Ilara Gale (the reigning champion, seed 1), circuit champions, Library bosses and Black Market regulars. You're seed 2.";
                var fieldText = Ui.Text(Ui.Box(block, 20, compact ? 80 : 104, compact ? 620 : 900, compact ? 22 : 24), field, 14, T.TextFaint, TextAnchor.MiddleLeft);
                fieldText.resizeTextForBestFit = true;
                fieldText.resizeTextMinSize = 11;
                fieldText.resizeTextMaxSize = 14;
                // A finished Invitational held back (its No Limits Packs can't be opened here): why, in place of the record.
                var held = running && profile.Bracket!.Done && kind == Brackets.InvitationalKind ? Brackets.FinishBlocked(profile.Bracket, _state.NoLimitsPacks()) : null;
                var recordLine = Ui.Text(Ui.Box(block, 20, compact ? 104 : 130, 620, compact ? 22 : 24),
                    held != null ? $"<color={T.WarnHex}>Finished, not paid yet: {held}</color>"
                    : record.Entries > 0 ? $"Your record: {record.Entries} entr{(record.Entries == 1 ? "y" : "ies")} · {record.Titles} title{(record.Titles == 1 ? "" : "s")} · best: {Brackets.PlacementText(Math.Max(0, record.Best)).ToLowerInvariant()}"
                        : "Your record: not entered yet", 14, T.TextQuiet, TextAnchor.MiddleLeft);
                recordLine.supportRichText = true;
                recordLine.resizeTextForBestFit = true;
                recordLine.resizeTextMinSize = 10;
                recordLine.resizeTextMaxSize = 14;
                // The button: the running bracket, Enter (pressed short of DP it says why), or why not.
                string label;
                bool open;
                if (running)
                {
                    var rb = profile.Bracket!;
                    label = rb.Done ? "Results ▶" : $"{rb.RoundName(rb.CurrentRound())} ▶";
                    open = true;
                }
                else if (blocked == null || (unlocked && profile.Bracket == null && profile.Dp < Brackets.EntryFor(kind)))
                {
                    label = $"Enter · {Brackets.EntryFor(kind):N0}";
                    open = true;
                }
                else
                {
                    label = blocked;
                    open = false;
                }
                ChalkboardRows.Add((kind, label, open));
                var k = kind;
                float buttonY = compact ? 86 : 150;
                if (open)
                {
                    var button = Ui.BoxButton(block, 660, buttonY, 260, 52, label, () => ChalkboardButton(k), 20);
                    button.targetGraphic.color = running || blocked == null ? T.Primary : T.Button;
                }
                else
                {
                    var why = Ui.Text(Ui.Box(block, 560, buttonY, 360, 52), label, 16, T.TextDim, TextAnchor.MiddleRight);
                    why.resizeTextForBestFit = true;
                    why.resizeTextMinSize = 11;
                    why.resizeTextMaxSize = 16;
                    if (!unlocked) Badge(block, "lock", new Vector2(940 / 2f - 34, blockH / 2f - 30), 30, T.TextDim);
                }
            }
            float y = top + Math.Max(1, shown.Count) * step;
            DrawYourDeck(y + 4, AlleyDeckProblems());
            DrawExhibition(y + 166);
        }

        /// <summary>A bracket's seed 1 by name (its `field:` line seeded 1: the Invitational's top seat), or "".</summary>
        private string TopSeat(AlleyBracketSpec spec)
        {
            foreach (var line in spec.Field)
            {
                var (w, _) = AlleyBracketSpec.ParseField(line);
                if (w.Length >= 2 && w[0] != BracketEntrant.YouId && w[w.Length - 1] == "1")
                    return _state.Cast.TryGetValue(w[0], out var who) ? who.Name : "";
            }
            return "";
        }

        // ---- The exhibition (Baz's chalkboard, HEADLINERS "The show") ------------------------------------------------------

        /// <summary>Whether you've met a ready headliner (the exhibition opens with the first).</summary>
        private bool ExhibitionOpen(Profile profile) =>
            _state.AlleyData.Headliners.Any(h => h.Ready && profile.Headliners.TryGetValue(h.Id, out var st) && (st.Met > 0 || st.Beaten > 0));

        /// <summary>The exhibition's sides as last offered (for the checks): the left's (headliners you've met), the right's (those and tonight's splashers).</summary>
        public List<(string key, string label)> ExhibitionLeft { get; } = new List<(string, string)>();
        public List<(string key, string label)> ExhibitionRight { get; } = new List<(string, string)>();
        private int _exLeft, _exRight;

        /// <summary>The sides you can pick: headliners you've met on the left; on the right the others and tonight's regulars at the open Street and Den tables.</summary>
        private void ExhibitionSides(Profile profile)
        {
            var data = _state.AlleyData;
            ExhibitionLeft.Clear();
            ExhibitionRight.Clear();
            foreach (var h in data.Headliners.Where(h => h.Ready && profile.Headliners.TryGetValue(h.Id, out var st) && (st.Met > 0 || st.Beaten > 0)))
                ExhibitionLeft.Add(("hl:" + h.Id, h.Handle));
            if (ExhibitionLeft.Count == 0) return;
            _exLeft = (_exLeft % ExhibitionLeft.Count + ExhibitionLeft.Count) % ExhibitionLeft.Count;
            var left = ExhibitionLeft[_exLeft].key;
            ExhibitionRight.AddRange(ExhibitionLeft.Where(x => x.key != left));
            for (int t = 0; t < data.Tables.Count; t++)
            {
                if (data.Tables[t].Tier is not (AlleyTier.Street or AlleyTier.Den) || !BackAlley.IsUnlocked(profile, data, t)) continue;
                foreach (var o in _state.AlleyLineup(t).Where(o => o.Kind == "regular"))
                    ExhibitionRight.Add(($"rg:{o.Table}.{o.Seat}", $"{o.Handle} · {data.Tables[t].Name}"));
            }
            if (ExhibitionRight.Count > 0) _exRight = (_exRight % ExhibitionRight.Count + ExhibitionRight.Count) % ExhibitionRight.Count;
        }

        /// <summary>
        /// The exhibition's block under your deck: a headliner you've met against another, or against one of tonight's
        /// splashers, picked with the arrows or drawn at random; Watch plays it CPU against CPU. Watch only: no stakes, no DP,
        /// no posters. Until you've met a headliner, it says when it opens.
        /// </summary>
        private void DrawExhibition(float y)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            const float h = 132;
            var block = Ui.Box(_detail, 30, y, 940, h);
            Ui.Image(block, T.RowB);
            var name = Ui.Text(Ui.Box(block, 20, 6, 400, 34), "EXHIBITION", 22, T.Text, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            T.Title(name);
            Ui.Text(Ui.Box(block, 420, 6, 500, 34), "watch only · no stakes, no DP, no posters", 14, T.TextDim, TextAnchor.MiddleRight);
            Ui.Text(Ui.Box(block, 20, 40, 900, 24), "Two headliners you've met, or a headliner against a splasher, CPU against CPU. Baz calls it, Mika streams it.", 14, T.TextSoft, TextAnchor.MiddleLeft);
            ExhibitionSides(profile);
            if (ExhibitionLeft.Count == 0 || ExhibitionRight.Count == 0)
            {
                Ui.Text(Ui.Box(block, 20, 72, 900, 48), ExhibitionLeft.Count == 0 ? "Opens with the first headliner you meet." : "Meet another headliner, or open a table, for its other side.",
                    16, T.TextDim, TextAnchor.MiddleLeft);
                return;
            }
            void Side(float x, string label, Action<int> step)
            {
                Ui.BoxButton(block, x, 74, 40, 46, "<", () => step(-1), 18);
                var text = Ui.Text(Ui.Box(block, x + 44, 74, 246, 46), label, 16, T.Text);
                text.fontStyle = FontStyle.Bold;
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = 10;
                text.resizeTextMaxSize = 16;
                Ui.BoxButton(block, x + 294, 74, 40, 46, ">", () => step(+1), 18);
            }
            Side(16, ExhibitionLeft[_exLeft].label, d => { _exLeft += d; _exRight = 0; Refresh(); });
            Ui.Text(Ui.Box(block, 352, 74, 40, 46), "vs", 16, T.TextDim);
            Side(394, ExhibitionRight[_exRight].label, d => { _exRight += d; Refresh(); });
            Ui.BoxButton(block, 740, 74, 84, 46, "Random", () =>
            {
                _exLeft = UnityEngine.Random.Range(0, ExhibitionLeft.Count);
                ExhibitionSides(profile);
                _exRight = UnityEngine.Random.Range(0, ExhibitionRight.Count);
                Refresh();
            }, 15);
            Ui.BoxButton(block, 830, 74, 96, 46, "Watch", () => DebugWatchExhibition(), 18).targetGraphic.color = T.Primary;
        }

        /// <summary>The exhibition's Watch, as clicked: the two sides shown.</summary>
        public void DebugWatchExhibition()
        {
            if (_state.Profile is not Profile profile) return;
            ExhibitionSides(profile);
            if (ExhibitionLeft.Count == 0 || ExhibitionRight.Count == 0) return;
            var (left, right) = (ExhibitionLeft[_exLeft].key, ExhibitionRight[_exRight].key);
            Sounds.Play("click");
            Close();
            ExhibitionClicked?.Invoke(left, right);
        }

        /// <summary>For checks: picks the exhibition's sides by key (false: not offered).</summary>
        public bool DebugPickExhibition(string left, string right)
        {
            if (_state.Profile is not Profile profile) return false;
            ExhibitionSides(profile);
            _exLeft = ExhibitionLeft.FindIndex(x => x.key == left);
            if (_exLeft < 0) return false;
            ExhibitionSides(profile);
            _exRight = ExhibitionRight.FindIndex(x => x.key == right);
            return _exRight >= 0;
        }

        /// <summary>A bracket's button on the chalkboard: short of DP it says why; else its entry dialog, or the running bracket.</summary>
        private void ChalkboardButton(string kind)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var blocked = Brackets.EnterBlocked(profile, _state.AlleyData, kind);
            bool running = profile.Bracket is Bracket b && b.Kind == kind;
            if (!running && blocked != null)
            {
                Sounds.Play("error");
                _status.text = $"<color={T.BadHex}>{blocked}</color> No stakes in a bracket: the entry, and placement pays.";
                return;
            }
            ChalkboardClicked?.Invoke(kind);
        }

        /// <summary>The campaign screen on the Back Alley, Baz's chalkboard shown (back from an alley bracket); <paramref name="message"/> in the status line.</summary>
        public void OpenChalkboard(string? message = null)
        {
            if (_state.Profile is Profile profile && AlleyShown(profile) && ChalkboardOpen(profile)) _selectedAlley = Chalkboard;
            if (!_alleyView && _state.Profile is Profile p && AlleyShown(p))
            {
                Open();
                ShowDistrictView(District.Alley);
                if (message != null) _status.text = message;
                return;
            }
            Open(message);
        }

        // ---- Orvel's Table -----------------------------------------------------------------------------------------------

        /// <summary>Orvel's panel as last drawn, for the checks: your spare cards and whether his binder matches each.</summary>
        public List<(CardCopy copy, Stake? his)> OrvelRows { get; } = new List<(CardCopy, Stake?)>();

        /// <summary>
        /// Orvel's Table: one seat (Patron Orvel's house deck), both stakes (your DP, held, and a spare card of yours you pick
        /// at the versus), his slab against your card from his binder (worth 1-2× it). With no spare card he'd take, there's no
        /// Duel: "Orvel looks through your binder. 'Nothing I'd take.'"
        /// </summary>
        private void ShowOrvelTable(int index)
        {
            var T = UiTheme.Current;
            var profile = _state.Profile!;
            var data = _state.AlleyData;
            var table = data.Tables[index];
            var ctx = _state.AlleyContext!;
            int stake = Ante.StakeFor(table.Tier);
            bool cleared = BackAlley.IsCleared(profile, data, index);
            var o = _state.AlleyLineup(index).FirstOrDefault();
            OrvelRows.Clear();
            PanelHeader(table.Name);
            AlleyStakeLine = $"Orvel's · stake {stake:N0} DP, held, and a spare card of yours · both back if you win";
            Ui.Text(Ui.Box(_detail, 30, 66, 940, 28), AlleyStakeLine + (cleared ? $"  <color={T.GoodHex}>✓ Cleared</color>" : $"  ·  <color={T.GoldSoftHex}>first win: +{AlleyEconomy.ClearTokens} Card Token</color>"),
                18, T.Gold, TextAnchor.MiddleLeft).supportRichText = true;
            Ui.Text(Ui.Box(_detail, 30, 98, 940, 26), "Alley list · no Ban Passes · turn 40 · higher LP wins  ·  A card you lose goes on Moth's rail.  ·  Leaving counts as a loss.", 15, T.TextSoft, TextAnchor.MiddleLeft);
            Ui.Image(Ui.Box(_detail, 30, 128, 940, 2), T.Divider).raycastTarget = false;
            if (o == null) { DrawYourDeck(150, AlleyDeckProblems()); return; }

            // The seat: Orvel himself.
            var binder = Ante.Binder(profile, o.Binder, ctx);
            var values = binder.Cards.Select(c => Ante.Value(c, ctx)).OrderBy(v => v).ToList();
            var row = Ui.Box(_detail, 30, 144, 940, 168);
            Ui.Image(row, T.RowA);
            Silhouette(row, 12, 12, 144, o);
            var head = Ui.Text(Ui.Box(row, 172, 10, 600, 36), $"{o.Handle}  <color={T.VioletHex}>HOUSE</color>", 24, T.Text, TextAnchor.MiddleLeft);
            head.fontStyle = FontStyle.Bold;
            head.supportRichText = true;
            Ui.Text(Ui.Box(row, 172, 46, 600, 24), $"Deck: {_state.DeckTitle(o.Base)} · Elo {o.Elo:N0}", 15, T.TextFaint, TextAnchor.MiddleLeft);
            var puts = Ui.Text(Ui.Box(row, 172, 72, 590, 90),
                $"Puts up: a Grade 9-10 slab, Super Rare and up, worth {AlleyEconomy.OrvelMatchMin:0.#}-{AlleyEconomy.OrvelMatchMax:0.#}× your card, authenticated by the Loupe.\n"
                + (values.Count > 0 ? $"<color={T.TextDimHex}>His binder: {values.Count} / {AlleyEconomy.BinderSize} slabs, {values[0]:N0} to {values[values.Count - 1]:N0} DP</color>"
                    : $"<color={T.TextDimHex}>Binder's empty: no stakes tonight.</color>"), 15, T.TextBody, TextAnchor.UpperLeft);
            puts.supportRichText = true;
            var spares = _state.OrvelSpares();
            foreach (var c in spares) OrvelRows.Add((c, _state.OrvelStake(o, c)));
            bool any = OrvelRows.Any(r => r.his != null);
            int tableIndex = index;
            if (spares.Count == 0)
                Ui.Text(Ui.Box(row, 772, 0, 160, 168), "Orvel looks through your binder.\n“Nothing I'd take.”", 15, T.TextDim).fontStyle = FontStyle.Italic;
            else Ui.BoxButton(row, 780, 54, 150, 60, "Duel", () => TryAlleyDuel(tableIndex), 22).targetGraphic.color = any ? T.Primary : T.Button;

            // Your side: the spare cards Orvel would take, cheapest first, and his slab against each.
            float y = 326;
            Ui.Text(Ui.Box(_detail, 30, y, 940, 24), $"YOUR SPARE CARDS ({spares.Count}) · worth {AlleyEconomy.OrvelMinCard:N0} DP or more, none a saved deck needs · you pick one at the versus",
                13, T.Caption, TextAnchor.MiddleLeft);
            y += 28;
            const int shown = 6;
            for (int i = 0; i < OrvelRows.Count && i < shown; i++)
            {
                var (copy, his) = OrvelRows[i];
                var line = Ui.Text(Ui.Box(_detail, 30, y, 940, 30),
                    $"{_state.CopyText(copy)} · <color={T.GoldHex}>{profile.Collection.Value(copy):N0} DP</color>   →   "
                    + (his != null ? $"he'd put up <b>{_state.StakeText(his)}</b> · <color={T.GoldHex}>{_state.StakeValue(his):N0} DP</color>"
                        : $"<color={T.TextDimHex}>“Nothing in my binder matches that.”</color>"),
                    15, T.TextBody, TextAnchor.MiddleLeft);
                line.supportRichText = true;
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 11;
                line.resizeTextMaxSize = 15;
                y += 32;
            }
            if (OrvelRows.Count > shown) { Ui.Text(Ui.Box(_detail, 30, y, 940, 24), $"…and {OrvelRows.Count - shown} more", 13, T.TextQuiet, TextAnchor.MiddleLeft); y += 26; }
            if (spares.Count == 0)
            {
                Ui.Text(Ui.Box(_detail, 30, y, 940, 30), "No spare card worth his time: a card a saved deck needs never rides.", 15, T.TextMuted, TextAnchor.MiddleLeft);
                y += 32;
            }
            _state.Save(); // his binder made or refilled to show it
            DrawYourDeck(Math.Max(y + 12, 600), AlleyDeckProblems());
        }

        // ---- The Unlisted binder (on the Library's shelf) ---------------------------------------------------------------

        /// <summary>The Unlisted binder's place on the shelf (after the cups and the final's plaque).</summary>
        private int UnlistedShelf => _state.Library.Cups.Count + 1;

        /// <summary>The Unlisted binder is shown on the right: the editor builds its pages under the Alley's rules.</summary>
        public bool UnlistedShown => _libraryView && _selectedLibrary == UnlistedShelf;

        private int _unlistedPage;

        /// <summary>How far the pages' column is scrolled (kept across the panel's rebuilds).</summary>
        private float _unlistedScroll;

        /// <summary>For checks: the page rows the panel built (every page has one).</summary>
        public int UnlistedRows { get; private set; }

        /// <summary>
        /// The binder after the cups (never "the 13th"): no ✓ or ★, its own plate, its page count. It appears with the first
        /// page (a contraband deck beaten in the alley).
        /// </summary>
        private void UnlistedBinder(Profile profile, float x, float y)
        {
            var T = UiTheme.Current;
            // theme: kept, the white selection frame (as the cups' binders)
            if (_selectedLibrary == UnlistedShelf) Ui.Image(Ui.Box(_circuitList, x - 4, y - 4, BinderWidth + 8, BinderHeight + 8), Color.white).raycastTarget = false;
            var binder = Ui.Box(_circuitList, x, y, BinderWidth, BinderHeight);
            Ui.Image(binder, Ui.Hex("#17120F"));
            Ui.Image(Ui.Box(binder, 0, 0, 14, BinderHeight), Ui.Hex("#3A2A22")).raycastTarget = false;
            // Its own plate: dark iron, the word stamped in.
            var rim = Ui.Box(binder, 26, 22, 182, 44);
            Ui.Image(rim, Ui.Hex("#2C2522")).raycastTarget = false;
            var plate = Ui.Box(rim, 2, 2, 178, 40);
            Ui.Image(plate, Ui.Hex("#4A403A")).raycastTarget = false;
            var word = Ui.Text(plate, "UNLISTED", 20, Ui.Hex("#D9CFC2"));
            word.fontStyle = FontStyle.Bold;
            word.raycastTarget = false;
            int pages = profile.UnlistedPages.Count;
            var count = Ui.Text(Ui.Box(binder, 26, 78, 182, 30), $"{pages} page{(pages == 1 ? "" : "s")}", 16, T.TextBody, TextAnchor.MiddleLeft);
            count.raycastTarget = false;
            var note = Ui.Text(Ui.Box(binder, 26, 108, 182, 44), "Decks beaten in the alley", 13, T.TextQuiet, TextAnchor.UpperLeft);
            note.fontStyle = FontStyle.Italic;
            note.raycastTarget = false;
            Ui.Clickable(binder, () =>
            {
                Sounds.Play("click");
                _selectedLibrary = UnlistedShelf;
                _status.text = "<b>Unlisted</b>: Hollis keeps a page for every contraband deck you've beaten downstairs. Pages never count toward a cup.";
                Refresh();
                ScrollToSelected();
            }, style: HoverStyle.Tile);
        }

        /// <summary>The page shown in the Unlisted panel (for the checks), and the deck it was last built as.</summary>
        public UnlistedPage? UnlistedPageShown { get; private set; }

        /// <summary>
        /// The Unlisted binder's panel: its pages on the left (handle and table, in the order you won them); the page on the
        /// right: the silhouette, the handle, the table, the deck list with its contraband marked, and "Open as a new deck"
        /// (built from the cards you own under the Alley's list, as the Recipe Book builds).
        /// </summary>
        private void ShowUnlisted()
        {
            var T = UiTheme.Current;
            for (int i = _detail.childCount - 1; i >= 0; i--) Destroy(_detail.GetChild(i).gameObject);
            var profile = _state.Profile!;
            var pages = Unlisted.Pages(profile);
            var title = Ui.Text(Ui.Box(_detail, 30, 18, 940, 46), "UNLISTED", 30, T.Text, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            Ui.Text(Ui.Box(_detail, 30, 64, 940, 26), "The contraband decks you've beaten in the alley, a page each, as you met them. They never count toward a cup.", 15, T.TextMuted, TextAnchor.MiddleLeft);
            if (pages.Count == 0 || _state.AlleyContext is not AlleyContext ctx) { UnlistedPageShown = null; return; }
            _unlistedPage = Mathf.Clamp(_unlistedPage, 0, pages.Count - 1);
            var data = _state.AlleyData;
            string TableName(UnlistedPage p) => data.IndexOf(p.Table) is var t && t >= 0 ? data.Tables[t].Name : p.Table;
            // The pages, down the left: one scrolling column (as the van's), so every page can be picked however many there are.
            const int rowH = 54, listH = 14 * rowH;
            var list = Ui.Box(_detail, 30, 100, 280, listH);
            var content = VanScreen.Scroll(list, 280, listH, pages.Count * rowH);
            var scroll = list.GetComponentInChildren<ScrollRect>();
            // A click rebuilds the panel: the column keeps where it was scrolled to.
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(_unlistedScroll, 0, Mathf.Max(0, pages.Count * rowH - listH)));
            scroll.onValueChanged.AddListener(_ => _unlistedScroll = content.anchoredPosition.y);
            UnlistedRows = 0;
            for (int i = 0; i < pages.Count; i++)
            {
                var page = pages[i];
                int at = i;
                bool on = i == _unlistedPage;
                var row = Ui.Box(content, 0, i * rowH, 280, rowH - 6);
                Ui.Image(row, on ? T.RowSelected : i % 2 == 0 ? T.RowA : T.RowB);
                if (on) Ui.Image(Ui.Box(row, 0, 0, 4, rowH - 6), T.Highlight).raycastTarget = false;
                var name = Ui.Text(Ui.Box(row, 12, 2, 262, 24), page.Handle, 15, T.Text, TextAnchor.MiddleLeft);
                name.fontStyle = FontStyle.Bold;
                name.resizeTextForBestFit = true;
                name.resizeTextMinSize = 10;
                name.resizeTextMaxSize = 15;
                Ui.Text(Ui.Box(row, 12, 24, 262, 22), TableName(page), 12, T.TextFaint, TextAnchor.MiddleLeft);
                Ui.Clickable(row, () => { _unlistedPage = at; Refresh(); }, style: HoverStyle.Row);
                UnlistedRows++;
            }

            // The page.
            var shown = pages[_unlistedPage];
            UnlistedPageShown = shown;
            var face = Ui.Box(_detail, 336, 100, 110, 110);
            Ui.Image(face, T.Well);
            // A headliner's or an authored deck's owner shows a portrait if there is one; a regular stays hooded.
            var who = shown.Key.StartsWith("hl:") ? shown.Key.Substring(3) : shown.Key == Unlisted.PennyKey ? BackAlley.PennyId
                : shown.Kind == "authored" ? data.Tables.SelectMany(t => t.Seats).FirstOrDefault(seat => seat.Deck == shown.Base)?.Character ?? "" : "";
            face.gameObject.AddComponent<RectMask2D>();
            if (!Portraits.Place(face, who)) AlleyMaterials.Silhouette(face, T.TextDim);
            var handle = Ui.Text(Ui.Box(_detail, 462, 100, 510, 40), shown.Handle, 26, T.Text, TextAnchor.MiddleLeft);
            handle.fontStyle = FontStyle.Bold;
            handle.resizeTextForBestFit = true;
            handle.resizeTextMinSize = 14;
            handle.resizeTextMaxSize = 26;
            var kind = shown.Kind == "regular" ? $"{_state.DeckTitle(shown.Base)}, splashed" : _state.DeckTitle(shown.Base);
            Ui.Text(Ui.Box(_detail, 462, 140, 510, 26), $"{TableName(shown)} · {kind}", 16, T.TextSoft, TextAnchor.MiddleLeft);
            var deck = Unlisted.DeckOf(shown, ctx);
            var contraband = new HashSet<uint>(Unlisted.Contraband(shown, ctx));
            Ui.Text(Ui.Box(_detail, 462, 168, 510, 26), $"<color={T.BadHex}>◆</color> contraband ({contraband.Count}) · {deck.Main.Count} main, {deck.Extra.Count} extra", 14, T.TextFaint, TextAnchor.MiddleLeft)
                .supportRichText = true;
            // The list: main then extra, name and count, the contraband marked.
            var lines = new List<string>();
            foreach (var part in new[] { deck.Main, deck.Extra })
                foreach (var g in part.GroupBy(c => Banlist.BaseCode(c, _cards)).OrderBy(g => _cards.Get(g.Key)?.Name))
                {
                    var name = _cards.Get(g.Key)?.Name ?? g.Key.ToString();
                    lines.Add(contraband.Contains(g.Key) ? $"<color={T.BadHex}>◆ {g.Count()} × {name}</color>" : $"   {g.Count()} × {name}");
                }
            const int perList = 26;
            for (int c = 0; c * perList < lines.Count && c < 2; c++)
            {
                var text = Ui.Text(Ui.Box(_detail, 336 + c * 318, 222, 312, perList * 20), string.Join("\n", lines.Skip(c * perList).Take(perList)), 13, T.TextBody, TextAnchor.UpperLeft);
                text.supportRichText = true;
                text.verticalOverflow = VerticalWrapMode.Truncate;
            }
            if (lines.Count > perList * 2) Ui.Text(Ui.Box(_detail, 336, 222 + perList * 20, 600, 20), $"…and {lines.Count - perList * 2} more", 12, T.TextQuiet, TextAnchor.MiddleLeft);
            var page2 = shown;
            Ui.BoxButton(_detail, 336, 790, 300, 52, "Open as a new deck", () => OpenUnlisted(page2), 18).targetGraphic.color = T.Primary;
            // A headliner's page plays its combo: the goldfish clip, the show and all, against an empty board.
            if (shown.Key.StartsWith("hl:") && data.FindHeadliner(shown.Key.Substring(3)) != null)
            {
                var id = shown.Key.Substring(3);
                Ui.BoxButton(_detail, 652, 790, 318, 52, "▶ Watch the combo", () => { Close(); WatchComboClicked?.Invoke(id); }, 18);
                WatchComboShown = id;
            }
            else
            {
                Ui.Text(Ui.Box(_detail, 650, 784, 320, 64), "Built from the cards you own under the Alley's list; the cards you're missing are left out.", 13, T.TextQuiet, TextAnchor.MiddleLeft);
                WatchComboShown = "";
            }
        }

        /// <summary>"Open as a new deck": the page built from your cards, saved as a new deck, and the editor opened on it.</summary>
        private void OpenUnlisted(UnlistedPage page)
        {
            if (_state.BuildUnlisted(page) is not (string name, int changes)) return;
            _status.text = $"Built <b>{name}</b> from your cards ({changes} change{(changes == 1 ? "" : "s")} for cards you're missing).";
            LastUnlistedDeck = name;
            EditDeck?.Invoke(name);
        }

        /// <summary>For checks: the headliner whose Watch the combo the page shown offers ("" for none).</summary>
        public string WatchComboShown { get; private set; } = "";

        /// <summary>The Unlisted binder on a headliner's page (back from its combo clip); <paramref name="message"/> in the status line.</summary>
        public void OpenUnlistedPage(string headliner, string? message = null)
        {
            Open();
            ShowDistrictView(District.Library);
            if (_state.Profile is Profile p && p.UnlistedPages.Count > 0)
            {
                _selectedLibrary = UnlistedShelf;
                int at = Unlisted.Pages(p).ToList().FindIndex(page => page.Key == "hl:" + headliner);
                if (at >= 0) _unlistedPage = at;
                Refresh();
                ScrollToSelected();
            }
            if (message != null) _status.text = message;
        }

        /// <summary>Watch the combo on the page shown, as clicked.</summary>
        public void DebugWatchCombo() { if (WatchComboShown.Length > 0) { Close(); WatchComboClicked?.Invoke(WatchComboShown); } }

        /// <summary>For checks: the deck "Open as a new deck" last built.</summary>
        public string LastUnlistedDeck { get; private set; } = "";

        // ---- For the checks -------------------------------------------------------------------------------------------

        /// <summary>Whether the Back Alley tab shows, and its label.</summary>
        public (bool shown, string label) DebugAlleyTab()
        {
            var label = _alleyTab.GetComponentInChildren<Text>().text;
            return (_districts.activeSelf && _alleyTab.gameObject.activeSelf, label);
        }

        /// <summary>The Back Alley tab, as clicked, showing <paramref name="table"/> (its scenes the first time).</summary>
        public void DebugShowAlley(int table)
        {
            Open();
            ShowDistrictView(District.Alley);
            if (table >= 0 && table != _selectedAlley && _alleyView) { _selectedAlley = table; Refresh(); _status.text = AlleyStatus; }
        }

        /// <summary>The Circuits tab, as clicked (from the alley: main street again).</summary>
        public void DebugShowCircuits()
        {
            Open();
            ShowDistrictView(District.Circuits);
        }

        public IReadOnlyList<(string name, bool open, string locked)> DebugAlleyNodes => _alleyNodes;

        /// <summary>The van node, as clicked.</summary>
        public void DebugVan() { Close(); VanClicked?.Invoke(); }

        /// <summary>The Library tab, as clicked, with the Unlisted binder on the right (false: there's no binder yet).</summary>
        public bool DebugShowUnlisted()
        {
            Open();
            ShowDistrictView(District.Library);
            if (_state.Profile is not Profile p || p.UnlistedPages.Count == 0) return false;
            _selectedLibrary = UnlistedShelf;
            Refresh();
            ScrollToSelected();
            return UnlistedShown;
        }

        /// <summary>A page's row, as clicked (scrolled to first), for the checks.</summary>
        public void DebugPickUnlisted(int page)
        {
            _unlistedScroll = Mathf.Max(0, page * 54 - 7 * 54);
            _unlistedPage = page;
            Refresh();
        }

        /// <summary>"Open as a new deck" on the page shown.</summary>
        public void DebugOpenUnlisted() { if (UnlistedPageShown != null) OpenUnlisted(UnlistedPageShown); }

        /// <summary>The panel's red Duel, as clicked.</summary>
        public void DebugAlleyDuel() => TryAlleyDuel(_selectedAlley);

        /// <summary>Baz's chalkboard's place on the map, for <see cref="DebugShowAlley"/>.</summary>
        public int DebugChalkboard => Chalkboard;
        /// <summary>For checks: the panel's text as drawn (the chalkboard's blocks, a table's seats).</summary>
        public string DebugPanelText => string.Join("\n", _detail.GetComponentsInChildren<Text>().Select(t => t.text));

        /// <summary>A bracket's button on the chalkboard, as clicked.</summary>
        public void DebugChalkboardButton(string kind) => ChalkboardButton(kind);

        /// <summary>The header's wallet line as shown.</summary>
        public string DebugWallet => _dp.text;
        /// <summary>For checks: the status line as drawn (a load's or a settle's notice, the alley's help).</summary>
        public string DebugStatus => _status.text;

        /// <summary>The screen's background color (the theme it was built in).</summary>
        public Color DebugBase => _root.GetComponent<Image>() is Image image && image != null ? image.color : Color.clear;
    }
}
