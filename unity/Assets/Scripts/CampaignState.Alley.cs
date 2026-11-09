using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Back Alley's side of the save (docs/dev/ALLEY-SPEC-2026-11-01.md, Phase A): its tables (campaign/alley-events.txt),
    /// the curated contraband (alley.txt), the Alley list (Traditional minus the exclusions) and the rules your decks answer
    /// to there, the regulars' voice (alley-voice.txt), and what a load does: the alley opens for a save that cleared a cup
    /// before it shipped, a save left mid-duel settles as a loss (with a notice), and Moth's rail ticks.
    /// </summary>
    public sealed partial class CampaignState
    {
        /// <summary>The alley's tables, headliners and brackets (empty without the file).</summary>
        public AlleyData AlleyData { get; private set; } = AlleyData.Empty;

        /// <summary>What the alley's engine calls need; null when the alley can't run here (no Traditional list or no printings).</summary>
        public AlleyContext? AlleyContext { get; private set; }

        /// <summary>Your decks' rules in the alley (the Alley list, no pool, no Ban Passes). Never seen by a circuit, cup or final.</summary>
        public FormatRules AlleyRules { get; private set; } = new FormatRules();

        /// <summary>
        /// Your decks' rules at No Limits (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 4.2: the No Limits list, three of anything,
        /// no pool, no Ban Passes). Only a nolimits-tier table (<see cref="AlleyRulesFor"/>) and the No Limits Invitational
        /// (<see cref="AlleyBracketRules"/>) see it; never another table or bracket, a cup or a circuit.
        /// </summary>
        public FormatRules NoLimitsRules { get; private set; } = new FormatRules();

        /// <summary>The regulars', Baz's and the headliners' lines (campaign/alley-voice.txt).</summary>
        public ShopVoice AlleyVoice { get; private set; } = ShopVoice.Parse("");

        /// <summary>Whether the alley can be played: data, the Alley list and printings are all there.</summary>
        public bool AlleyReady => AlleyContext != null && AlleyData.Tables.Count > 0;

        /// <summary>A notice for the campaign screen to show once (a duel left mid-way, Moth selling a railed card); "" for none.</summary>
        public string AlleyNotice = "";

        /// <summary>
        /// The story still to play when the campaign screen opens, a batch per source in the order queued: a notice's
        /// (antelose, railgone), or any campaign result's (a circuit, Tag Cup, guest, Library cup, final round, bracket or
        /// alley duel) whose results screen never showed (another duel started, or the menu, editor or shop opened in its 2 s).
        /// Each batch keeps its own placeholders, card-unlock grants and Tag Cup partner (who speaks a "partner:" line), so two
        /// results' scenes never borrow each other's card, handle or partner. The save keeps it (<see cref="Profile.StoryQueue"/>,
        /// with the batches a results screen took and hasn't played yet: <see cref="_storyInHand"/>), written whenever it changes
        /// and read back at a load (<see cref="AfterAlleyLoad"/>), so quitting the app can't lose a duel's first-time scenes: the
        /// next campaign screen plays them.
        /// </summary>
        private readonly List<QueuedStory> _storyQueue = new List<QueuedStory>();

        /// <summary>
        /// Batches a results screen took (<see cref="TakeQueuedStory"/>) and hasn't played yet: still in the save, so a quit with
        /// the results screen up loses nothing. They leave when played (<see cref="StoryPlayed"/>), go back in the queue
        /// (<see cref="Requeue"/>) or are cleared (<see cref="ClearAlleyStory"/>).
        /// </summary>
        private readonly List<QueuedStory> _storyInHand = new List<QueuedStory>();

        /// <summary>True during <see cref="AfterAlleyLoad"/>: the queue's changes there are saved with the load's own save.</summary>
        private bool _loadingStory, _storyDirty;

        /// <summary>Every queued event, in order (for the campaign screen and the checks).</summary>
        public IReadOnlyList<string> AlleyNoticeEvents => _storyQueue.SelectMany(b => b.Events).Distinct().ToList();

        /// <summary>
        /// Queues a result's or a notice's scenes for the next campaign screen, as a batch of their own (events already queued are
        /// left out), and writes the queue into the save. <paramref name="derived"/>: a batch every load raises again (the alley
        /// titles' catch-up), kept out of the save so a load doesn't change it.
        /// </summary>
        public void QueueAlleyStory(IEnumerable<string> events, IReadOnlyDictionary<string, string> fill, IEnumerable<Grant>? grants = null, string partner = "",
            bool derived = false)
        {
            var queued = new HashSet<string>(AlleyNoticeEvents);
            var batch = new QueuedStory
            {
                Events = events.Where(e => queued.Add(e)).ToList(), Fill = fill.ToDictionary(p => p.Key, p => p.Value),
                Grants = grants?.ToList() ?? new List<Grant>(), Partner = partner, Derived = derived,
            };
            if (batch.Events.Count == 0 && batch.Grants.Count == 0) return;
            _storyQueue.Add(batch);
            if (!derived) SaveStoryQueue();
        }

        /// <summary>Clears the queued scenes (they played, or the results screen plays them), in the save too.</summary>
        public void ClearAlleyStory()
        {
            if (_storyQueue.Count == 0 && _storyInHand.Count == 0) return;
            _storyQueue.Clear();
            _storyInHand.Clear();
            SaveStoryQueue();
        }

        /// <summary>
        /// A batch's scenes played (or had nothing left to play): off the save's queue (a batch never queued: nothing to do).
        /// <paramref name="save"/> false: the caller saves right after (the story's own end-of-batch save), so it's one write.
        /// </summary>
        public void StoryPlayed(QueuedStory batch, bool save = true)
        {
            if (_storyInHand.Remove(batch) | _storyQueue.Remove(batch)) SaveStoryQueue(save);
        }

        /// <summary>
        /// A progress-only save (the story queue changed, a scene was seen): the save written without rotating profile.txt.bak,
        /// so the backup stays the last real save's (the one before the duel), not the queue's echo of it.
        /// </summary>
        public void SaveProgress()
        {
            if (Profile == null) return;
            try { Profile.Save(SavePath, backup: false); }
            catch (IOException ex) { UnityEngine.Debug.LogWarning($"Could not save the campaign: {ex.Message}"); }
        }

        /// <summary>
        /// The queue into the save (<see cref="Profile.StoryQueue"/>: the batches in hand first, they were queued earlier), then a
        /// progress-only save (<see cref="SaveProgress"/>: the backup isn't rotated), unless <paramref name="save"/> is false (the
        /// caller saves right after) or during a load (whose own save writes it). The grants go in display only
        /// (<see cref="StoryBatch.Shown"/>): they were paid at the duel, and replaying a batch only reads their names.
        /// </summary>
        private void SaveStoryQueue(bool save = true)
        {
            if (Profile == null) return;
            Profile.StoryQueue.Clear();
            foreach (var b in _storyInHand.Concat(_storyQueue).Where(b => !b.Derived))
                Profile.StoryQueue.Add(new StoryBatch
                {
                    Events = b.Events.ToList(), Fill = b.Fill.ToDictionary(p => p.Key, p => p.Value),
                    Grants = b.Grants.Select(StoryBatch.Shown).ToList(), Partner = b.Partner,
                });
            if (_loadingStory) _storyDirty = true;
            else if (save) SaveProgress();
        }

        /// <summary>The save's queue as the app's (at a load): every batch back in the queue, nothing in hand.</summary>
        private void LoadStoryQueue()
        {
            _storyQueue.Clear();
            _storyInHand.Clear();
            if (Profile == null) return;
            foreach (var b in Profile.StoryQueue)
                _storyQueue.Add(new QueuedStory
                {
                    Events = b.Events.ToList(), Fill = b.Fill.ToDictionary(p => p.Key, p => p.Value),
                    Grants = b.Grants.Select(StoryBatch.Shown).ToList(), Partner = b.Partner,
                });
        }

        /// <summary>A batch of queued scenes: their events, placeholders, grants and partner.</summary>
        public sealed class QueuedStory
        {
            public List<string> Events = new List<string>();
            public Dictionary<string, string> Fill = new Dictionary<string, string>();
            public List<Grant> Grants = new List<Grant>();
            public string Partner = "";
            /// <summary>Raised again by every load (<see cref="QueueAlleyStory"/>'s derived): never written into the save.</summary>
            public bool Derived;
        }

        /// <summary>
        /// Takes the queue (a results screen showing: its own duel's batch and any a skipped screen or a notice left, to play
        /// on its Continue). With <paramref name="among"/>, only the batches with an event in it (the campaign screen's
        /// events). Those taken are off the queue but stay in the save until played (<see cref="StoryPlayed"/>) or put back.
        /// </summary>
        public List<QueuedStory> TakeQueuedStory(ICollection<string>? among = null)
        {
            var taken = _storyQueue.Where(b => among == null || b.Events.Any(among.Contains)).ToList();
            _storyQueue.RemoveAll(taken.Contains);
            _storyInHand.AddRange(taken);
            return taken;
        }

        /// <summary>Puts taken batches back, ahead of anything queued since (a results screen left another way than Continue).</summary>
        public void Requeue(IEnumerable<QueuedStory> batches)
        {
            var back = batches.ToList();
            _storyInHand.RemoveAll(back.Contains);
            _storyQueue.InsertRange(0, back);
            SaveStoryQueue();
        }

        private void InitAlley(string folder)
        {
            AlleyData = AlleyData.Load(Path.Combine(folder, AlleyData.FileName), _cards);
            AlleyVoice = ShopVoice.Load(Path.Combine(folder, "alley-voice.txt"));
            AlleyVoice.CardName = code => _cards.Get(code)?.Name ?? code.ToString();
            VanVoice = ShopVoice.Load(Path.Combine(folder, "van.txt"));
            VanVoice.CardName = AlleyVoice.CardName;
            var traditional = _banlists.FirstOrDefault(l => l.Name.Contains("Traditional"));
            if (traditional != null && _printings != null && AlleyData.Tables.Count > 0)
            {
                var list = Alley.List(traditional, _cards);
                var noLimits = Alley.NoLimitsList(_cards);
                var curated = Path.Combine(folder, "alley.txt");
                AlleyContext = new AlleyContext
                {
                    Cards = _cards, Printings = _printings, Lists = _banlists, List = list, NoLimits = noLimits,
                    LoadDeck = name => _library.Contains(name) ? NamedDeck(name) : null,
                    Ratings = _ratings, Curated = File.Exists(curated) ? Alley.LoadCurated(curated, _cards) : new Dictionary<string, Curation>(),
                    Roster = _roster, Library = Library, Data = AlleyData,
                };
                AlleyRules = Alley.Rules(list);
                NoLimitsRules = Alley.Rules(noLimits);
            }
            AfterAlleyLoad();
        }

        private Deck NamedDeck(string name)
        {
            var deck = _library.Load(name);
            deck.Name = name;
            return deck;
        }

        /// <summary>
        /// What a load does for the alley: opens it once a cup is cleared (the "alley" scene plays the next time the campaign
        /// screen opens), gives a beaten headliner its Unlisted page if the save predates the binder, settles a duel left held
        /// or an alley bracket duel left live (the app closed or crashed mid-duel: a loss, with a notice; a Locals one replays),
        /// catches up an alley title's scene (the app closed during its Contraband reveal), and lets Moth sell what's past its
        /// time on the rail. It also reads back the story queue the save kept (the next campaign screen plays it), and gives a
        /// held-back Invitational finish its way out (<see cref="SettleHeldInvitational"/>, also on the bracket screen in the
        /// session; owed packs paid once they can open).
        /// Saves when anything changed.
        /// </summary>
        private void AfterAlleyLoad()
        {
            if (Profile == null) return;
            _loadingStory = true;
            try { AfterAlleyLoadSettle(); }
            finally { _loadingStory = false; }
        }

        /// <summary>
        /// A held-back Invitational finish settled (<see cref="Brackets.SettleHeld"/>): finished now, its DP, tokens and record
        /// paid, its No Limits Packs owed until the pool can fill one; the notice and its scenes for the next campaign screen.
        /// The same path at a load and from the bracket screen's "Settle now" (in the session). Null when nothing is held.
        /// The caller saves (a load saves when anything changed).
        /// </summary>
        public BracketReward? SettleHeldInvitational(ContrabandPacks? noLimits = null)
        {
            if (Profile == null) return null;
            noLimits ??= Profile.Bracket is { Done: true } b && b.Kind == Brackets.InvitationalKind ? NoLimitsPacks() : null;
            if (Brackets.SettleHeld(Profile, Library, noLimits) is not BracketReward held) return null;
            AlleyNotice = string.Join("  ·  ", new[] { AlleyNotice, $"{held.Name}: {held.Summary}." }.Where(s => s.Length > 0));
            QueueAlleyStory(held.Events, BackAlley.FillFor(Profile, AlleyData, null, null, _cards));
            return held;
        }

        private void AfterAlleyLoadSettle()
        {
            // The story the last run queued and never played (it quit in a result's 2 s, or with its results screen up): back in
            // the queue, for the next campaign screen.
            if (Profile == null) return;
            LoadStoryQueue();
            _storyDirty = false;
            bool changed = AlleyReady && BackAlley.Open(Profile, Library);
            // A Phase A save's beaten headliners get their Unlisted pages (quietly: no scene).
            changed |= Unlisted.Backfill(Profile, AlleyData) > 0;
            if (Profile.AnteLive != null && AlleyContext != null && Ante.SettleAbandoned(Profile, AlleyContext) is AnteSettlement settled)
            {
                AlleyNotice = settled.Summary;
                QueueAlleyStory(settled.Events, settled.Fill);
                changed = true;
            }
            if (AlleyContext != null && Brackets.SettleAbandoned(Profile, Library, Matchups) is BracketResult left)
            {
                AlleyNotice = string.Join("  ·  ", new[] { AlleyNotice, left.Reward.Summary }.Where(s => s.Length > 0));
                QueueAlleyStory(left.Events, BackAlley.FillFor(Profile, AlleyData, null, null, _cards), left.Reward.Grants);
                changed = true;
            }
            // A held-back Invitational finish (its No Limits Packs can't be opened: Brackets.FinishBlocked) gets its way out at a
            // load: finished now, its DP, tokens and record paid, its packs owed (Brackets.SettleHeld); then owed packs open
            // once the pool can fill a pack (Brackets.PayOwedPacks). Either one, never both for the same pool.
            // (The No Limits pool is drawn only when there's something for it: a finished Invitational, or packs owed.)
            var noLimits = Profile.Bracket is { Done: true } finished && finished.Kind == Brackets.InvitationalKind || Profile.NoLimitsPacksOwed.Count > 0
                ? NoLimitsPacks() : null;
            if (SettleHeldInvitational(noLimits) != null) changed = true;
            if (Brackets.PayOwedPacks(Profile, noLimits) is { Count: > 0 } owed)
            {
                AlleyNotice = string.Join("  ·  ", new[] { AlleyNotice, $"Baz paid the {owed.Count} No Limits Pack{(owed.Count > 1 ? "s" : "")} he owed you: {owed.Sum(k => k.Count)} cards, in your collection." }
                    .Where(s => s.Length > 0));
                changed = true;
            }
            // A title's scene the reveal never reached: queued for the next campaign screen (it plays once: seen).
            // Every load raises these again, so they stay out of the save (derived): a load never changes it for them.
            if (AlleyReady) QueueAlleyStory(Brackets.AlleyTitleEvents(Profile, AlleyData), new Dictionary<string, string>(), derived: true);
            // The wall (No Limits): a save already past Nyx's clear and the alley-closing scene opens it now (its scene on the next campaign screen).
            if (OpenWall()) { QueueAlleyStory(new[] { BackAlley.WallEvent }, new Dictionary<string, string>()); changed = true; }
            changed |= TickRail();
            if (changed || _storyDirty) Save();
        }

        /// <summary>
        /// The wall opens (docs/dev/NO-LIMITS-SPEC-2026-11-02.md section 4.1): when <see cref="BackAlley.WallDue"/> (Nyx's Table cleared,
        /// the alley-closing scene seen, the wall not open yet), <see cref="BackAlley.WallEvent"/> goes into <see cref="Profile.Seen"/>
        /// (that's the unlock: no save field) and the caller raises the event. True the one time it opens. Checked at load
        /// (<see cref="AfterAlleyLoad"/>) and each time the Back Alley tab is shown.
        /// </summary>
        public bool OpenWall(bool show = true)
        {
            if (Profile == null || !AlleyReady) return false;
            // Never the same night as Nyx's clear (NO-LIMITS-STORY continuity 1): alley-closing seen since the last campaign
            // screen (the Continue after her fourth win, or its queued scenes) holds the wall until the next one. A save
            // loaded already past it counts as an earlier night.
            bool closing = Profile.Seen.Contains(BackAlley.ClosingScene), fresh = closing && _closingKnown == false;
            _closingKnown = closing;
            if (fresh || !show || !BackAlley.WallDue(Profile, AlleyData)) return false;
            Profile.Seen.Add(BackAlley.WallEvent);
            return true;
        }

        /// <summary>Whether alley-closing had been seen at the last <see cref="OpenWall"/> (null: not asked yet this run).</summary>
        private bool? _closingKnown;

        /// <summary>Whether the wall is due to open (for the checks).</summary>
        public bool WallDue => Profile != null && AlleyReady && BackAlley.WallDue(Profile, AlleyData);

        /// <summary>Whether a table (an index into the alley's tables) is the No Limits table (nolimits tier).</summary>
        public bool IsNoLimitsTable(int table) => table >= 0 && table < AlleyData.Tables.Count && AlleyData.Tables[table].Tier == AlleyTier.NoLimits;

        /// <summary>
        /// Your decks' rules at a table (<see cref="BackAlley.RulesFor"/>): the No Limits rules at the No Limits table, the Alley's
        /// everywhere else (and for -1, or without the alley). Every caller that used <see cref="AlleyRules"/> for a table goes
        /// through it: the versus check, the deck editor from a panel, the duel's rules.
        /// </summary>
        public FormatRules AlleyRulesFor(int table) =>
            AlleyContext == null || !IsNoLimitsTable(table) ? AlleyRules : BackAlley.RulesFor(AlleyData, table, AlleyContext);

        /// <summary>
        /// Your decks' rules in an alley bracket of this kind, every round (<see cref="Brackets.RulesFor"/>): the No Limits rules
        /// at the Invitational, the Alley's at the Brawl and the Midnight Open (<see cref="AlleyRules"/> itself, so those read
        /// exactly as before). The versus check, Make it yours and the duel's rules all go through it.
        /// </summary>
        public FormatRules AlleyBracketRules(string kind)
        {
            if (AlleyContext == null || !Brackets.IsAlleyKind(kind)) return AlleyRules;
            var rules = Brackets.RulesFor(kind, AlleyContext);
            return rules.List == AlleyRules.List ? AlleyRules : rules.List == NoLimitsRules.List ? NoLimitsRules : rules;
        }

        /// <summary>
        /// The No Limits Pack (the Invitational's prize, <see cref="ContrabandPacks.NoLimits"/>: the ready builds' ever-Forbidden
        /// cards), drawn fresh each time (the pool follows which builds are ready); null without the alley.
        /// </summary>
        public ContrabandPacks? NoLimitsPacks() =>
            AlleyContext == null || _printings == null ? null : ContrabandPacks.NoLimits(AlleyData, AlleyContext);

        /// <summary>Moth sells what's past its time on the rail (any duel counts): a notice and "railgone" (its {sold}) for the campaign screen. True when anything went.</summary>
        private bool TickRail()
        {
            if (Profile == null) return false;
            var gone = Ante.TickRail(Profile, code => _cards.Get(code)?.Name ?? code.ToString());
            if (gone.Count == 0) return false;
            AlleyNotice = string.Join("  ·  ", new[] { AlleyNotice }.Concat(gone.Select(n => $"Moth sold your {n}.")).Where(s => s.Length > 0));
            QueueAlleyStory(new[] { "railgone" }, new Dictionary<string, string> { [BackAlley.SoldKey] = gone[0] });
            return true;
        }

        /// <summary>
        /// The rail's clock outside the alley's own duels (a circuit or cup duel counts too): called when the campaign screen
        /// or the van opens; saves when Moth sold something.
        /// </summary>
        public void TickAlleyRail()
        {
            if (TickRail()) Save();
        }

        /// <summary>For checks: the save read again from disk, as the app does at launch (the load-time settling included: the alley's and a live duel's).</summary>
        public void DebugReload()
        {
            Profile = Profile.Load(SavePath);
            WireCollection(Profile);
            AfterAlleyLoad();
            AfterLiveLoad();
        }

        /// <summary>A table's index by its id or name (-1: none).</summary>
        public int AlleyTable(string idOrName) => AlleyData.IndexOf(idOrName);

        /// <summary>Tonight's lineup at a table (drawn and saved the first time); empty while locked or without the alley.</summary>
        public IReadOnlyList<AlleyOpponent> AlleyLineup(int table)
        {
            if (Profile == null || AlleyContext == null) return new List<AlleyOpponent>();
            int before = Profile.AlleyOpponents.Count;
            var lineup = BackAlley.Lineup(Profile, AlleyData, table, AlleyContext);
            if (Profile.AlleyOpponents.Count != before || lineup.Any(o => !Profile.AlleyOpponents.Contains(o))) Save();
            return lineup;
        }

        /// <summary>The opponent at a seat tonight (0: the encore), or null.</summary>
        public AlleyOpponent? AlleySeat(int table, int seat)
        {
            if (Profile == null || table < 0 || table >= AlleyData.Tables.Count) return null;
            if (seat > 0) return AlleyLineup(table).FirstOrDefault(o => o.Seat == seat);
            return Profile.AlleyOpponents.FirstOrDefault(o => o.Table == AlleyData.Tables[table].Id && o.Seat == 0);
        }

        /// <summary>An alley opponent's deck as its LP plate names it: "Goat Control, splashed", a headliner's deck by its title.</summary>
        private string AlleyDeckTitle(string deckKey)
        {
            var key = deckKey.Substring(Alley.DeckPrefix.Length);
            var dot = key.LastIndexOf('.');
            bool numbered = int.TryParse(dot > 0 ? key.Substring(dot + 1) : "", out var seat);
            // An alley bracket's splash regular ("alley:bracket.<slot>"): the running bracket's slot.
            var o = !numbered ? null
                : key.Substring(0, dot) == BackAlley.BracketTable
                    ? Profile?.Bracket is Bracket b && seat >= 0 && seat < b.Size ? b.Slots[seat]?.Splash : null
                    : Profile?.AlleyOpponents.FirstOrDefault(x => x.Table == key.Substring(0, dot) && x.Seat == seat);
            if (o == null) return "Contraband deck";
            return o.Kind == "regular" ? $"{DeckTitle(o.Base)}, splashed" : DeckTitle(o.Base);
        }

        /// <summary>An alley opponent's deck by its key ("alley:dumpster-row.2"), rebuilt from the save; null if unknown.</summary>
        public Deck? AlleyDeck(string deckKey) =>
            Profile == null || AlleyContext == null ? null : BackAlley.ResolveDeck(Profile, deckKey, AlleyContext);

        /// <summary>The stake a CPU puts up next (its binder made and refilled on access: save after), or null when its binder is empty.</summary>
        public Stake? AlleyStake(AlleyOpponent o)
        {
            if (Profile == null || AlleyContext == null || o.Binder.Length == 0) return null;
            return Ante.Next(Profile, o.Binder, AlleyContext);
        }

        /// <summary>A CPU's binder count now ("9 / 12").</summary>
        public int AlleyBinderCount(AlleyOpponent o) =>
            Profile == null || AlleyContext == null || o.Binder.Length == 0 ? 0 : Ante.Binder(Profile, o.Binder, AlleyContext).Cards.Count;

        /// <summary>A stake as the alley says it: "Pot of Greed · Super Rare · Raw · Near Mint" or "... · Grade 9 slab".</summary>
        public string StakeText(Stake s) =>
            $"{_cards.Get(s.Code)?.Name ?? s.Code.ToString()} · {Rarities.Name(s.Rarity)} · {(s.Slab > 0 ? $"Grade {s.Slab} slab" : Grades.RawLabel(s.Grade))}";

        /// <summary>A stake's value (Pricing.V at its printing and condition).</summary>
        public int StakeValue(Stake s) => AlleyContext == null ? 0 : Ante.Value(s, AlleyContext);

        /// <summary>A card's name by its code (the code itself when unknown).</summary>
        public string CardName(uint code) => _cards.Get(code)?.Name ?? code.ToString();

        /// <summary>Decks you can take into the alley: your own campaign decks only (no loaners there, and no partner's edit).</summary>
        public List<string> AlleyDeckChoices() =>
            _library.Names.Where(_library.IsCampaign).Where(IsYours).ToList();

        /// <summary>
        /// A deck of yours under the Alley's rules, or (<paramref name="table"/> the No Limits table) the No Limits rules: its
        /// list (with the Alley's own words: "Pot of Greed: the Alley allows 1, you have 2."; at No Limits "Djinn Releaser of
        /// Rituals: No Limits doesn't allow it (you have 1)."), deck rules, and cards you don't own.
        /// </summary>
        public List<DeckProblem> AlleyProblems(Deck deck, int table = -1) => AlleyProblems(deck, AlleyRulesFor(table));

        /// <summary>A deck of yours under these alley rules (<see cref="AlleyRulesFor"/>, <see cref="AlleyBracketRules"/>), in the alley's words.</summary>
        public List<DeckProblem> AlleyProblems(Deck deck, FormatRules rules)
        {
            var problems = Problems(deck, rules);
            var list = rules.List;
            if (list == null) return problems;
            var where = list == AlleyContext?.NoLimits ? "No Limits" : "the Alley";
            foreach (var p in problems.Where(p => p.Code != 0 && p.Message.Contains(list.Name)))
            {
                int count = deck.Main.Concat(deck.Extra).Concat(deck.Side).Count(c => Banlist.BaseCode(c, _cards) == p.Code);
                int allowed = Math.Min(3, list.Allowed(p.Code, _cards));
                var name = _cards.Get(p.Code)?.Name ?? p.Code.ToString();
                p.Message = allowed == 0 ? $"{name}: {where} doesn't allow it (you have {count})." : $"{name}: {where} allows {allowed}, you have {count}.";
            }
            return problems;
        }

        /// <summary>A voice line for a hook, its speaker named ("opponent" is <paramref name="opponent"/>); null when none.</summary>
        public (string speaker, string text)? AlleyLine(string hook, string opponent, IReadOnlyDictionary<string, string>? fill = null, int? showTurn = null)
        {
            if (Profile == null) return null;
            // A show-only duel (the exhibition, a combo clip) says its lines without marking them heard (ShopVoice.Peek).
            var line = showTurn is int turn ? AlleyVoice.Peek(hook, Profile, Circuits, fill ?? new Dictionary<string, string>(), turn)
                : AlleyVoice.Pick(hook, Profile, Circuits, fill ?? new Dictionary<string, string>());
            if (line == null) return null;
            var speaker = line.Speaker == ShopVoice.OpponentSpeaker ? opponent : line.Speaker == "-" ? "" : CastName(line.Speaker);
            return (speaker, line.Text);
        }

        // ---- Phase B: Orvel's Table, Moth's van, the Unlisted binder ----------------------------------------------------

        /// <summary>Moth's lines (campaign/van.txt).</summary>
        public ShopVoice VanVoice { get; private set; } = ShopVoice.Parse("");

        private ContrabandPacks? _contraband;

        /// <summary>The Contraband Pack (its pool is the van's: the used tab and the shelf draw from it); null without the alley.</summary>
        public ContrabandPacks? Contraband => AlleyContext == null || _printings == null ? null
            : _contraband ??= new ContrabandPacks(_cards, _printings, _banlists, AlleyContext.List);

        /// <summary>Moth's contraband shelf this period (sold ones marked); empty without the alley.</summary>
        public List<Listing> VanShelf() =>
            Profile == null || AlleyContext == null || Contraband is not ContrabandPacks packs ? new List<Listing>() : MothsVan.Shelf(Profile, packs, AlleyContext);

        /// <summary>Moth's used contraband this period (sold ones marked); empty without the alley.</summary>
        public List<Listing> VanUsed() =>
            Profile == null || AlleyContext == null || Contraband is not ContrabandPacks packs ? new List<Listing>() : MothsVan.Used(Profile, packs, AlleyContext);

        /// <summary>A line of Moth's for a hook (null when none), with its placeholders filled.</summary>
        public (string speaker, string text)? VanLine(string hook, IReadOnlyDictionary<string, string>? fill = null)
        {
            if (Profile == null) return null;
            var line = VanVoice.Pick(hook, Profile, Circuits, fill ?? new Dictionary<string, string>());
            if (line == null) return null;
            return (line.Speaker == "-" ? "" : CastName(line.Speaker), line.Text);
        }

        /// <summary>
        /// Copies you could stake at Orvel's Table, cheapest first (the least to lose leads): spares no saved deck needs, not at
        /// the Grading Desk, worth at least <see cref="AlleyEconomy.OrvelMinCard"/>.
        /// </summary>
        public List<CardCopy> OrvelSpares() =>
            Profile == null ? new List<CardCopy>()
                : Ante.Stakeable(Profile, NeededByDecks, AlleyEconomy.OrvelMinCard).OrderBy(c => Profile.Collection.Value(c)).ThenBy(c => c.Id).ToList();

        /// <summary>Orvel's stake against your card: the next slab in his binder worth 1-2× it (null: nothing matches). Save after.</summary>
        public Stake? OrvelStake(AlleyOpponent o, CardCopy yours) =>
            Profile == null || AlleyContext == null || o.Binder.Length == 0 ? null : Ante.Next(Profile, o.Binder, AlleyContext, yours);

        /// <summary>A copy as the alley says it: "Pot of Greed · Super Rare · Grade 9 slab".</summary>
        public string CopyText(CardCopy c) =>
            $"{_cards.Get(c.Code)?.Name ?? c.Code.ToString()} · {Rarities.Name(c.Rarity)} · {(c.IsSlab ? $"Grade {c.Slab} slab" : Grades.RawLabel(c.Grade))}";

        /// <summary>Whether Ines's card is due ("heldcard": Orvel's Table cleared and the cup-goat-clear scene seen, the held-card scene not yet).</summary>
        public bool HeldCardDue => Profile != null && BackAlley.HeldCardDue(Profile, AlleyData);

        /// <summary>
        /// Whether the finale is due (<see cref="Story.FinaleDue"/>): the Library's and the alley's last scenes seen, plus No
        /// Limits' while its table is ready, the finale not yet. Raised on the campaign screen's Open, next to the held card.
        /// </summary>
        public bool FinaleDue => Profile != null && Story.FinaleDue(Profile, AlleyData);

        /// <summary>
        /// An Unlisted page as a deck of yours ("Open as a new deck", as the Recipe Book's Build): its list built from the cards
        /// you own under the Alley's list, saved as a new campaign deck named for its handle. Returns the deck's name and the
        /// builder's changes (cards you're missing), or null without the alley.
        /// </summary>
        public (string name, int changes)? BuildUnlisted(UnlistedPage page)
        {
            if (Profile == null || AlleyContext == null) return null;
            var recipe = Unlisted.DeckOf(page, AlleyContext);
            var result = AutoDeck.FromRecipe(recipe, Profile.Collection, AlleyContext.List, _cards);
            var deck = result.Deck;
            var file = DeckLibrary.SafeName(page.Handle);
            var name = $"{file} (unlisted)";
            for (int n = 2; _library.Contains(name); n++) name = $"{file} (unlisted {n})";
            deck.Name = name;
            deck.Campaign = true;
            // It plays by its base's playbook, as the CPU did.
            if (recipe.Notes.TryGetValue("recipe", out var playbook)) deck.Notes["recipe"] = playbook;
            _library.Save(deck);
            return (name, Math.Max(0, result.Changes.Count - 1));
        }

        /// <summary>Everyone with a profile, duelists or not (Baz, Moth and Odile never duel): for speakers' names.</summary>
        public IReadOnlyDictionary<string, Personality> Cast = new Dictionary<string, Personality>();

        /// <summary>A speaker's short name: "Baz" for Baz "the Bell" Calloway, else as the shop says it ("Hollis").</summary>
        public string CastName(string id)
        {
            if (!Cast.TryGetValue(id, out var who)) return SpeakerName(id);
            return who.Name.Contains("\"") ? who.Name.Split(' ')[0] : SpeakerName(id) == id ? who.Name.Split(' ').Last() : SpeakerName(id);
        }
    }
}
