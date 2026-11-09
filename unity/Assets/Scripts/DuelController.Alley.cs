using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;
using Ygo.Engine.DeckBuilding;
using Ygo.Engine.Presentation;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Back Alley's duels (docs/dev/ALLEY-SPEC-2026-11-01.md sections 7-10, Phases A and B): the versus (your own decks only,
    /// checked against the Alley list; their contraband shown, and the card they put up with its value; at Orvel's Table a
    /// spare card of yours picked with the stake arrows, and his slab against it), the stakes held in the save before the
    /// coin toss (<see cref="Ante.Hold"/>), the duel with the turn-40 cap and the alley's music
    /// (<see cref="BossMusic.ForAlley"/>), the result recorded and the stakes settled (<see cref="BackAlley.Record"/>), and
    /// the results screen (a card lost at Orvel's goes on Moth's rail, and the results say for how much and how long).
    /// Leaving a held duel (to the menu, another duel, a replay) settles it as a loss at once; conceding is a loss as a
    /// result. Everything here is in the BackAlley theme. Moth's van (<see cref="VanScreen"/>) opens from the map: its
    /// first visit plays "fence", a first buy-back "buyback".
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The alley table being dueled (an index into the alley's tables), or null.</summary>
        private int? _alleyTable;
        private AlleyOpponent? _alleyOpp;
        private Personality? _alleyWho;
        private TurnCap? _turnCap;
        /// <summary>Checks only: the turn cap's last turn (0: the alley's knob).</summary>
        private int _alleyTurnCapOverride;
        /// <summary>The opponent's intro, picked once per seat they sit (cycling decks keeps it).</summary>
        private string _alleyQuote = "", _alleyQuoteFor = "";
        /// <summary>A refusal or a note for the next alley versus (an illegal deck, a stake you can't cover).</summary>
        private string _alleyNote = "";
        /// <summary>What the last alley versus showed (for the check): their showcase and their stake.</summary>
        private List<uint> _alleyVersusShowcase = new List<uint>();
        /// <summary>The label over each showcase card the last alley versus showed (No Limits: UNBANNED, CONTRABAND; for the check).</summary>
        private List<string> _alleyVersusLabels = new List<string>();
        private Stake? _alleyVersusStake;
        private string _alleyVersusChip = "";
        /// <summary>The last alley versus's deck line and detail for their side (No Limits: "<#title> + <picks>", the UNBANNED row and Baz's line; for the check).</summary>
        private string _alleyVersusDeckLine = "", _alleyVersusDetail = "";
        /// <summary>The note the last alley versus showed (a refusal), and the last cup result's story events (for the check).</summary>
        private string _alleyLastNote = "";
        private List<string> _lastCupEvents = new List<string>();

        /// <summary>Moth's van, Under the Counter.</summary>
        private VanScreen _van = null!;
        /// <summary>The spare card of yours picked for Orvel's Table (the stake arrows on his versus), or null.</summary>
        private CardCopy? _orvelPick;
        /// <summary>For the check: the cards the last Orvel versus offered, and his stake against the one picked.</summary>
        private List<CardCopy> _orvelChoices = new List<CardCopy>();

        private void InitAlley()
        {
            _campaignScreen.AlleyDuel += table => ShowAlleyVersus(table, null);
            _campaignScreen.AlleyEncore += (table, headliner) => ShowAlleyVersus(table, headliner);
            _van = gameObject.AddComponent<VanScreen>();
            _van.Init(_campaign, _cardPack!);
            _van.Closed += () => _campaignScreen.Open();
            // A scene over the van (its first buy-back): the van steps aside and comes back, without greeting you again.
            _van.StoryEvents += (events, fill) =>
            {
                if (_campaign.Profile is not Profile p || _story.Pending(p, events).Count == 0) return;
                _van.Hide();
                PlayStory(events, "", () => _van.Open(visit: false), null, fill);
            };
            _campaignScreen.VanClicked += OpenVan;
            InitShow();
        }

        /// <summary>The van from the alley map: Moth's sold anything off the rail first; its first visit plays "fence" before it opens.</summary>
        private void OpenVan()
        {
            UiTheme.Current = UiTheme.BackAlley;
            _campaign.TickAlleyRail();
            PlayStory(new[] { BackAlley.FenceEvent }, "", () => _van.Open());
        }

        /// <summary>Main street's screens (the shop, the Recipe Book, the guests, the lessons, the menu) open in its theme.</summary>
        private static void MainStreetTheme()
        {
            if (UiTheme.Current != UiTheme.MainStreet) UiTheme.Current = UiTheme.MainStreet;
        }

        private AlleyTable? AlleyTableAt(int table) =>
            table >= 0 && table < _campaign.AlleyData.Tables.Count ? _campaign.AlleyData.Tables[table] : null;

        /// <summary>
        /// Before an alley duel: the next unbeaten seat (or an encore). A headliner's scene the first time; your own decks
        /// (the arrows cycle them, no loaners); their contraband and their stake; Duel! checks your deck under the Alley's
        /// rules, holds the stakes in the save, and tosses the coin. Back costs nothing and rerolls nothing.
        /// </summary>
        private void ShowAlleyVersus(int table, string? encore, bool cycled = false)
        {
            var profile = _campaign.Profile;
            var ctx = _campaign.AlleyContext;
            var t = AlleyTableAt(table);
            if (profile == null || ctx == null || t == null) { _campaignScreen.Open(); return; }
            UiTheme.Current = UiTheme.BackAlley;
            var data = _campaign.AlleyData;
            var o = encore != null ? BackAlley.Encore(profile, data, table, encore, ctx) : _campaign.AlleySeat(table, BackAlley.NextSeat(profile, data, table));
            _campaign.Save();
            if (o == null) { _campaignScreen.Open("That seat isn't open."); return; }
            // A headliner's first meeting: its scene, then the versus (a No Limits build's: its pilot's, "pilot:<id>").
            var meeting = o.IsBuild ? BackAlley.PilotEvent + o.Character : "headliner:" + o.Character;
            if (!cycled && o.IsHeadliner && _story.Pending(profile, new[] { meeting }).Count > 0)
            {
                PlayStory(new[] { meeting }, "", () => ShowAlleyVersus(table, encore));
                return;
            }
            var choices = _campaign.AlleyDeckChoices();
            if (choices.Count == 0) { _campaignScreen.Open("Build a deck of your own first: the alley doesn't lend."); return; }
            if (_versusDeck == null || !choices.Contains(_versusDeck) || !cycled) _versusDeck = choices.Contains(profile.Deck) ? profile.Deck : choices[0];
            var yourDeck = _library.Load(_versusDeck);
            yourDeck.Name = _versusDeck;
            // Your deck under the table's rules (BackAlley.RulesFor: No Limits' at No Limits, the Alley's elsewhere).
            var problems = _campaign.AlleyProblems(yourDeck, table);
            bool noLimits = t.Tier == AlleyTier.NoLimits;
            var build = o.IsBuild ? data.FindBuild(o.Character) : null;
            int stakeDp = Ante.StakeFor(t.Tier);
            // Orvel's Table: your card first (the stake arrows), then his slab against it.
            bool orvel = t.Tier == AlleyTier.Orvel;
            if (orvel)
            {
                _orvelChoices = _campaign.OrvelSpares();
                if (_orvelChoices.Count == 0) { _campaignScreen.Open("Orvel looks through your binder. “Nothing I'd take.”"); return; }
                if (_orvelPick == null || !_orvelChoices.Contains(_orvelPick))
                    _orvelPick = _orvelChoices.FirstOrDefault(c => _campaign.OrvelStake(o, c) != null) ?? _orvelChoices[0];
            }
            var theirs = orvel ? _campaign.OrvelStake(o, _orvelPick!) : _campaign.AlleyStake(o);
            bool penny = theirs?.Flag == Ante.PennyFlag;
            _campaign.Save();
            var who = BackAlley.PersonalityOf(o, ctx);
            string Name(uint c) => _cardPack!.Get(c)?.Name ?? c.ToString();

            // Their intro: a regular's from the alley's pools (the card that made its handle, else its table's), a pilot's pool
            // (pilot:intro:<id>) falling back to its profile's, a headliner's own.
            var quoteKey = $"{o.Table}.{o.Seat}.{o.Night}.{o.Character}";
            if (_alleyQuoteFor != quoteKey)
            {
                _alleyQuoteFor = quoteKey;
                _alleyQuote = o.Kind == "regular" ? RegularLine("intro", o, theirs)
                    : build != null ? (o.Kind == "encore" ? _campaign.AlleyLine($"pilot:encore:{o.Character}", o.Handle, Fill(o, theirs))?.text : null)
                        ?? _campaign.AlleyLine($"pilot:intro:{o.Character}", o.Handle, Fill(o, theirs))?.text ?? who.Intro
                    : who.Intro;
                _campaign.Save();
            }
            // No Limits: the UNBANNED row (the build's first restored card and its count), Baz's word on it (baz:restored:<id>, else
            // baz:restored, else baz:stake:nolimits), and the pieces' labels: UNBANNED over a restored card, CONTRABAND over a pick.
            var unbanned = build != null ? BackAlley.Unbanned(o, ctx) : new List<uint>();
            var restoredFill = Fill(o, theirs);
            if (unbanned.Count > 0) restoredFill["card"] = Name(unbanned[0]);
            string unbannedRow = unbanned.Count > 0 ? $"UNBANNED: {Name(unbanned[0])} ×{BackAlley.CopiesIn(o, unbanned[0], ctx)}" : "";
            // Baz's word on it: a pilot's restored card, a headliner's tell, Orvel's house, an empty binder, or the stake.
            var baz = build != null ? (unbanned.Count > 0 ? _campaign.AlleyLine($"baz:restored:{o.Character}", o.Handle, restoredFill) ?? _campaign.AlleyLine("baz:restored", o.Handle, restoredFill) : null)
                                      ?? _campaign.AlleyLine("baz:stake:nolimits", o.Handle, Fill(o, theirs))
                : o.IsHeadliner ? _campaign.AlleyLine("tell:" + o.Character, o.Handle, Fill(o, theirs))
                : orvel ? _campaign.AlleyLine("baz:orvel", o.Handle, Fill(o, theirs))
                : theirs == null ? _campaign.AlleyLine("baz:empty", o.Handle, Fill(o, null))
                // A table's own pool first (baz:contraband:nyx: Nyx's seats), else everyone's.
                : _campaign.AlleyLine("baz:contraband:" + t.Id, o.Handle, Fill(o, theirs));
            _campaign.Save();

            var added = o.Added.Select(c => Banlist.BaseCode(c, _cardPack!)).Distinct()
                .OrderByDescending(c => _cardPack!.Get(c) is CardInfo i ? AutoDeck.Value(i) : 0).Select(Name).ToList();
            var showcase = BackAlley.Showcase(o, ctx).ToList();
            var picks = build?.Picks.ToList() ?? new List<uint>();
            var labels = build == null ? new List<string>()
                : showcase.Select(c => build.Restored.Contains(c) ? "UNBANNED" : picks.Contains(c) ? "CONTRABAND" : "").ToList();
            _alleyVersusShowcase = showcase;
            _alleyVersusLabels = labels;
            _alleyVersusStake = theirs;
            var T = UiTheme.Current;
            var you = new CampaignScenes.Side
            {
                Name = "You", Deck = DeckTitle(_versusDeck),
                Showcase = CampaignScenes.ShowcaseOf(yourDeck, _cardPack!),
                Detail = (orvel ? $"Your stakes: <color={T.GoldHex}>{stakeDp:N0} DP</color>, held, and the card below · both back if you win"
                          : penny ? "Nothing of yours is held: Penny plays for the Common."
                          : theirs != null ? $"Your stake: <color={T.GoldHex}>{stakeDp:N0} DP</color>, held · back if you win" : "Nothing of yours is held: their binder's empty")
                         + $"\nYou have {profile.Dp:N0} DP · {profile.AnteWins} stake{(profile.AnteWins == 1 ? "" : "s")} won here",
            };
            _alleyVersusChip = theirs != null ? $"THEIR STAKE   <b>{_campaign.StakeText(theirs)}</b>  ·  <color={T.GoldHex}>{_campaign.StakeValue(theirs):N0} DP</color>"
                : orvel ? "HIS STAKE   “Nothing in my binder matches that.” Pick another card."
                : "THEIR STAKE   none tonight: the binder's empty";
            // A build's deck line: "<#title> + <picks>" (its contraband picks by name); its title: the chair it sits in.
            var pickNames = picks.Select(Name).ToList();
            string deckLine = o.Kind == "regular" ? $"{DeckTitle(o.Base)}{(added.Count > 0 ? " + " + string.Join(", ", added.Take(3)) : "")}"
                : build != null ? $"{DeckTitle(o.Base)}{(pickNames.Count > 0 ? " + " + string.Join(", ", pickNames) : "")}"
                : DeckTitle(o.Base);
            string title = build != null ? (o.Seat == 0 ? $"Encore · {t.Name}" : o.Seat == t.SeatCount ? $"The end chair · {t.Name}" : $"Seat {o.Seat} · {t.Name}")
                : o.IsHeadliner ? $"Headliner · {t.Name}" : o.Kind == "authored" ? $"The house · {t.Name}" : $"{t.Name} regular";
            var detailLines = new List<string>();
            if (unbannedRow.Length > 0) detailLines.Add($"<color={T.SignHex}>{unbannedRow}</color>");
            if (baz is { } b) detailLines.Add($"{b.speaker}: “{b.text}”");
            _alleyVersusDeckLine = deckLine;
            _alleyVersusDetail = string.Join("\n", detailLines);
            var them = new CampaignScenes.Side
            {
                Name = o.Handle, Title = title,
                Deck = deckLine,
                DeckNote = o.Elo > 0 ? $"(Elo {o.Elo:N0})" : "",
                // The label only over contraband (an authored deck's showcase falls back to its own cards when it has none); a
                // build's pieces each labelled on their own.
                Showcase = showcase, ShowcaseLabel = showcase.Any(BackAlley.ShowcaseContraband(o, ctx).Contains) ? "CONTRABAND" : "", ShowcaseLabels = labels,
                Portraits = o.IsHeadliner || o.Kind == "authored" ? new List<string> { o.Character }
                    : o.Kind == "regular" && YgoCampaign.Game.Portraits.GruntFor(o.Handle) is string grunt ? new List<string> { grunt } : new List<string>(),
                Quote = _alleyQuote,
                Detail = _alleyVersusDetail,
                Chip = _alleyVersusChip,
            };
            var scouts = Scouts((o.Handle, o.Kind == "regular" ? o.Base : Playbooks.RecipeOf(BackAlley.DeckOf(o, ctx))));
            if (added.Count > 0 && scouts.Count > 0) scouts[0].Keys.Insert(0, $"<color={T.BadHex}>Contraband:</color> {string.Join(", ", added)}");
            if (build != null && scouts.Count > 0)
            {
                if (pickNames.Count > 0) scouts[0].Keys.Insert(0, $"<color={T.BadHex}>Contraband:</color> {string.Join(", ", pickNames)}");
                if (unbanned.Count > 0) scouts[0].Keys.Insert(0, $"<color={T.SignHex}>Unbanned:</color> {string.Join(", ", unbanned.Select(Name))}");
            }
            var note = _alleyNote;
            _alleyNote = "";
            if (note.Length == 0 && orvel && theirs == null)
                note = $"<color={T.BadHex}>“Nothing in my binder matches that.”</color> Pick another card with the stake arrows, or leave.";
            if (note.Length == 0 && problems.Count > 0)
                note = $"<color={T.BadHex}>{DeckTitle(_versusDeck)} isn't legal here:</color> {problems[0].Message}";
            _alleyLastNote = note;
            string match = (o.Seat == 0 ? "Encore" : $"Seat {o.Seat} of {t.SeatCount}")
                + (noLimits ? " · turn 40, higher LP wins · no list, three of anything · leaving counts as a loss" : " · turn 40, higher LP wins · Alley list, no Ban Passes · leaving counts as a loss");
            // The music: Orvel's (or a closer's, or the end chair's) theme stings in; the alley's own is built meanwhile.
            var music = BossMusic.ForAlley(_campaign.AlleyData, table, o.Seat);
            if (!cycled && music.theme != BossMusic.Alley) Sounds.Sting(music.theme, music.transpose);
            else Sounds.Prepare(music.theme, music.transpose);
            _scenes.ShowVersus(t.Name, match, you, them,
                start: () => StartAlleyStakes(table, encore, o),
                back: () => _campaignScreen.Open(),
                cycleDeck: delta => { CycleVersusDeck(choices, delta); ShowAlleyVersus(table, encore, cycled: true); },
                scouts: scouts, quiet: true, note: note, arrowsHint: "Arrows: switch between your own decks (the alley doesn't lend).",
                texts: _campaign.TextsLine(yourDeck, _campaign.AlleyRulesFor(table)));
            if (orvel) OrvelPicker(table, encore);
        }

        /// <summary>
        /// Orvel's stake picker on his versus, in your side's quote space: your card (name, condition, value), "n of N", and
        /// arrows through your spare cards, cheapest first. Drawn into the versus screen, so it goes when the screen does.
        /// </summary>
        private void OrvelPicker(int table, string? encore)
        {
            var root = _scenes.transform.Find("CampaignScenesCanvas/CampaignScenes") as RectTransform;
            if (root == null || _orvelPick == null) return;
            var T = UiTheme.Current;
            var profile = _campaign.Profile!;
            int at = _orvelChoices.IndexOf(_orvelPick);
            var chip = Ui.Box(root, 110, 760, 720, 64);
            Ui.Image(chip, T.Well);
            Ui.Image(Ui.Box(chip, 0, 0, 4, 64), T.Highlight).raycastTarget = false;
            var text = Ui.Text(Ui.Box(chip, 16, 4, 540, 56),
                $"YOUR CARD   <b>{_campaign.CopyText(_orvelPick)}</b>  ·  <color={T.GoldHex}>{profile.Collection.Value(_orvelPick):N0} DP</color>\n"
                + $"<size=13><color={T.TextDimHex}>{at + 1} of {_orvelChoices.Count} spare cards · lost, it goes on Moth's rail</color></size>", 18, T.Text, TextAnchor.MiddleLeft);
            text.supportRichText = true;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 11;
            text.resizeTextMaxSize = 18;
            void Step(int delta)
            {
                _orvelPick = _orvelChoices[(at + delta + _orvelChoices.Count) % _orvelChoices.Count];
                ShowAlleyVersus(table, encore, cycled: true);
            }
            Ui.BoxButton(chip, 572, 6, 64, 52, "<", () => Step(-1), 26).targetGraphic.color = T.Button;
            Ui.BoxButton(chip, 644, 6, 64, 52, ">", () => Step(+1), 26).targetGraphic.color = T.Button;
            _scenes.Buttons["stake<"] = () => Step(-1);
            _scenes.Buttons["stake>"] = () => Step(+1);
        }

        /// <summary>The placeholders a line about this opponent and stake fills: {card}, {table}, {handle}, {nick}, {grade} (the engine's).</summary>
        private Dictionary<string, string> Fill(AlleyOpponent o, Stake? stake) =>
            BackAlley.FillFor(_campaign.Profile!, _campaign.AlleyData, o, stake, _cardPack!);

        /// <summary>A regular's line (intro, win, lose): the card that made its handle, else its table's, else everyone's.</summary>
        private string RegularLine(string moment, AlleyOpponent o, Stake? stake)
        {
            var fill = Fill(o, stake);
            if (moment == "intro")
            {
                var handleCard = o.Added.Select(a => Banlist.BaseCode(a, _cardPack!))
                    .OrderByDescending(a => _cardPack!.Get(a) is CardInfo i ? AutoDeck.Value(i) : 0).ThenBy(a => a)
                    .FirstOrDefault(a => _campaign.AlleyData.Handles.Any(h => h.card == a));
                if (handleCard != 0 && _cardPack!.Get(handleCard) is CardInfo card
                    && _campaign.AlleyLine($"regular:intro:card:{card.Name}", o.Handle, fill) is { } byCard)
                    return byCard.text;
            }
            return _campaign.AlleyLine($"regular:{moment}:{o.Table}", o.Handle, fill)?.text ?? "";
        }

        /// <summary>
        /// Duel!: your deck must be legal under the Alley's rules; then the stakes are held (your DP out of your wallet,
        /// their card out of the binder) and saved, and the coin is tossed. From here leaving is a loss.
        /// </summary>
        private void StartAlleyStakes(int table, string? encore, AlleyOpponent o)
        {
            var profile = _campaign.Profile!;
            var ctx = _campaign.AlleyContext!;
            var deckName = _versusDeck ?? profile.Deck;
            // Under the table's rules (No Limits' at No Limits: 3 Pot of Greed pass here and nowhere else; Djinn passes nowhere).
            if (!_library.Contains(deckName) || _campaign.AlleyProblems(_library.Load(deckName), table) is { Count: > 0 } problems)
            {
                var why = _library.Contains(deckName) ? _campaign.AlleyProblems(_library.Load(deckName), table)[0].Message : "Build a deck first.";
                var where = _campaign.IsNoLimitsTable(table) ? "at No Limits" : "in the alley";
                _alleyNote = $"<color={UiTheme.Current.BadHex}>{DeckTitle(deckName)} isn't legal {where}:</color> {why} Switch decks with the arrows, or edit it.";
                ShowAlleyVersus(table, encore, cycled: true);
                return;
            }
            var tier = BackAlley.TierOf(_campaign.AlleyData, table);
            // At Orvel's Table your card rides too (the one the stake arrows picked).
            var yours = tier == AlleyTier.Orvel ? _orvelPick : null;
            var live = Ante.Hold(profile, _campaign.AlleyData, table, o, yours, _campaign.NeededByDecks, ctx);
            if (live == null)
            {
                var theirs = yours != null ? _campaign.OrvelStake(o, yours) : _campaign.AlleyStake(o);
                _alleyNote = $"<color={UiTheme.Current.BadHex}>{Ante.Blocked(profile, tier, theirs, yours, _campaign.NeededByDecks, ctx) ?? "The stakes can't be held."}</color>";
                ShowAlleyVersus(table, encore, cycled: true);
                return;
            }
            // Held in the save before the coin is tossed: closing the app now is a loss at the next launch.
            _campaign.Save();
            seed++;
            var theirDeck = o.Kind == "regular" ? o.Base : Playbooks.RecipeOf(BackAlley.DeckOf(o, ctx));
            CoinToss("You", o.Handle, deckName, theirDeck, () => StartAlleyDuel(table, o));
        }

        private void StartAlleyDuel(int table, AlleyOpponent o)
        {
            var ctx = _campaign.AlleyContext!;
            _alleyOpp = o;
            _alleyWho = BackAlley.PersonalityOf(o, ctx);
            if (o.Kind == "regular") _alleyWho.Intro = _alleyQuote;
            _pickCharacter[0] = -1;
            _pickDeck[0] = TakeVersusDeck();
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == o.Character));
            _pickDeck[1] = o.DeckKey;
            _paused = false;
            NewDuel(alleyTable: table);
            UpdateControls();
        }

        /// <summary>
        /// Leaving a held alley duel before it ends (to the menu, another duel, a replay): a loss, settled at once
        /// (<see cref="Ante.SettleAbandoned"/>), and the campaign screen says so next time it opens.
        /// </summary>
        private void AbandonAlleyDuel()
        {
            if (_alleyTable == null || _runner == null || _runner.IsFinished || _watching != null) return;
            _alleyTable = null;
            if (_campaign.Profile is not Profile profile || _campaign.AlleyContext is not AlleyContext ctx || profile.AnteLive == null) return;
            var settled = Ante.SettleAbandoned(profile, ctx);
            _campaign.Save();
            if (settled == null) return;
            _campaign.AlleyNotice = settled.Summary;
            _campaign.QueueAlleyStory(settled.Events, settled.Fill);
            _view.AppendLog(settled.Summary);
        }

        /// <summary>
        /// At the end of an alley duel: records it at its seat (the stakes settle; a concession is a loss and never takes the
        /// first-turn guard), saves, and shows the results. False when this isn't one.
        /// </summary>
        private bool FinishAlleyDuel()
        {
            if (_alleyTable is not int table || _watching != null || _campaign.Profile is not Profile profile || _campaign.AlleyContext is not AlleyContext ctx)
                return false;
            var t = AlleyTableAt(table);
            var o = _alleyOpp;
            var who = _alleyWho ?? _players[1];
            if (t == null || o == null) return false;
            int winner = _board.Winner > 1 ? 2 : _board.Winner;
            int reason = _runner?.Result.Reason ?? 0;
            int turns = _runner?.Result.Turns ?? _board.Turn;
            var stake = profile.AnteLive?.Theirs;
            // A concession never takes the first-turn guard; "turncap" is the cap's own decision (whatever its last turn).
            var yours = profile.AnteLive?.Yours;
            // A headliner's route stopped before its payoff, and you won: its Combo Breaker (DuelController.Show).
            var result = BackAlley.Record(profile, _campaign.AlleyData, table, winner, turns, FirstTurnPlayer, ctx,
                conceded: reason == DuelRunner.ReasonConceded, turnCap: reason == DuelRunner.ReasonTurnCap, comboBroken: o.IsHeadliner && ComboBroken);
            // Ines's card: due once Orvel's Table is cleared and the goat cup's scene seen (Record leaves it to the callers).
            if (_campaign.HeldCardDue) result.Events.Add(BackAlley.HeldEvent);
            if (result.Ante?.Railed != null) _orvelPick = null;
            _campaign.AfterDuel(result.Reward, "", _tally, false, tag: false);
            // The scenes' "opponent" speaker (nl-restored) is this pilot: its id rides with the placeholders, queued or played here.
            if (o.IsBuild) result.Fill[StoryScreen.OpponentSpeaker] = o.Character;
            // Its scenes wait in the queue until the results screen takes them: if the menu, the editor or the shop opens in
            // the 2 s before it, the next campaign screen plays them (alleyclear:, headlinerbeat:, the first antewin).
            var queued = result.Events.ToList();
            AddGrantEvents(queued, result.Reward);
            _campaign.Save();
            // The turn-40 line, as the log has it.
            string capLine = "";
            if (reason == DuelRunner.ReasonTurnCap && _turnCap != null)
                capLine = winner > 1 ? DuelNarrator.TurnCapText(_turnCap.LastTurn, null, 0, 0)
                    : DuelNarrator.TurnCapText(_turnCap.LastTurn, winner == 0 ? "You" : who.Name, _turnCap.Lp(winner), _turnCap.Lp(1 - winner));
            // Their last word: a regular's from the pools; a pilot's pool (pilot:win|lose:<id>; pilot:restored:<id> when its own
            // restored card just left with you), falling back to its profile's; anyone else's own.
            var pilotFill = Fill(o, stake);
            bool restoredWon = o.IsBuild && result.Ante?.Won is CardCopy wonCopy && (_campaign.AlleyData.FindBuild(o.Character)?.Restored.Contains(wonCopy.Code) ?? false);
            var quote = winner == 2 ? ""
                : o.Kind == "regular" ? RegularLine(winner == 0 ? "lose" : "win", o, stake)
                : o.IsBuild ? ((winner == 0 && restoredWon ? _campaign.AlleyLine($"pilot:restored:{o.Character}", o.Handle, pilotFill) : null)
                               ?? _campaign.AlleyLine($"pilot:{(winner == 0 ? "lose" : "win")}:{o.Character}", o.Handle, pilotFill))?.text
                              ?? (winner == 0 ? who.LoseLine : who.WinLine)
                : winner == 0 ? who.LoseLine : who.WinLine;
            _campaign.Save();
            var headline = winner > 1 ? "Draw!" : winner == 0 ? "You win!" : $"{who.Name} wins!";
            _view.SetResult(headline, capLine.Length > 0 ? capLine : quote, $"{result.Summary}   ·   {profile.Dp:N0} DP");
            if (capLine.Length > 0) _view.AppendLog(capLine);
            if (quote.Length > 0) _view.AppendLog($"{who.Name}: \"{quote}\"");
            _view.AppendLog($"{t.Name}: {result.Summary}.");
            ShowResultsSoon(queued, result.Reward.Grants, story => ShowAlleyResults(table, o, who, result, winner, quote, capLine, stake, yours, story), fill: result.Fill);
            return true;
        }

        /// <summary>After an alley duel: the board for a moment, then the results (the stakes, not DP), the scenes, the alley.</summary>
        private void ShowAlleyResults(int table, AlleyOpponent o, Personality who, AlleyResult result, int winner, string quote,
            string capLine, Stake? stake, CardCopy? yours, List<CampaignState.QueuedStory> story)
        {
            // (The queued scenes, taken off the queue, are this screen's to play.)
            UiTheme.Current = UiTheme.BackAlley;
            var T = UiTheme.Current;
            var profile = _campaign.Profile!;
            var t = AlleyTableAt(table)!;
            var state = profile.AlleyTables.TryGetValue(t.Id, out var st) ? st : new AlleyTableState();
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? "Victory!" : "Defeat",
                Speaker = who.Name, Quote = quote,
                Circuit = $"{t.Name} · {(o.Seat == 0 ? "Encore" : $"Seat {o.Seat}")} against {o.Handle}",
                TotalDp = profile.Dp,
                // Tonight's seats (all four when the night just ended).
                Progress = Enumerable.Range(1, t.SeatCount).Select(s => result.NightOver || (state.WonMask & (1 << (s - 1))) != 0).ToList(),
                ProgressItem = "Seat", ProgressTitle = result.NightOver ? "THE NIGHT'S OVER" : "TONIGHT",
                Record = $"{t.Name}: {(BackAlley.IsCleared(profile, _campaign.AlleyData, table) ? "cleared" : "not cleared yet")}\n"
                         + $"Stakes won <b>{profile.AnteWins}</b> · lost <b>{profile.AnteLosses}</b>",
            };
            var ante = result.Ante;
            if (ante == null || ante.Outcome == "none" || (ante.Returned == 0 && ante.Lost == 0 && ante.Won == null && stake == null))
                r.Stakes.Add(("Stakes", "none: their binder was empty", T.TextMuted));
            else
            {
                if (ante.Lost > 0) r.Stakes.Add(("Your stake", $"-{ante.Lost:N0} DP", T.Bad));
                else if (ante.Returned > 0) r.Stakes.Add(("Your stake", $"{ante.Returned:N0} DP back", T.Good));
                if (ante.Won != null)
                    r.Stakes.Add(("Their card", $"{_campaign.StakeText(stake ?? new Stake { Code = ante.Won.Code, Rarity = ante.Won.Rarity, Grade = ante.Won.Grade, Slab = ante.Won.Slab })}"
                        + (ante.Added is { Kept: false } ? " (a 4th copy: you keep your best three)" : " · yours"), T.Gold));
                else if (stake != null) r.Stakes.Add(("Their card", $"{_cardPack!.Get(stake.Code)?.Name} · back in their binder", T.TextMuted));
                if (ante.Outcome == "firstturn") r.Stakes.Add(("First-turn rule", "lost before your first turn: nothing charged", T.Good));
                // Your card at Orvel's: back, or on Moth's rail at its tag.
                if (ante.Railed is RailEntry railed)
                {
                    int left = Ante.DuelsLeft(profile, railed);
                    r.Stakes.Add(("Your card", $"{_campaign.CopyText(railed.Copy)} · on Moth's rail, {railed.Price:N0} DP for {left} duel{(left == 1 ? "" : "s")}", T.Bad));
                    r.Unlocks.Add($"Moth's rail: buy your {_cardPack!.Get(railed.Copy.Code)?.Name} back at the van for {railed.Price:N0} DP within {left} duel{(left == 1 ? "" : "s")}");
                }
                else if (yours != null) r.Stakes.Add(("Your card", $"{_campaign.CopyText(yours)} · back", T.Good));
            }
            r.Stakes.Add(("Duel Points", "none: alley duels pay in cards", T.TextMuted));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (result.Reward.Exp, profile.Level, into, needed);
            if (capLine.Length > 0) r.Unlocks.Add(capLine);
            if (result.Replay) r.Unlocks.Add("A draw: the seat is played again, stakes back");
            if (result.FirstClear) r.Unlocks.Add($"{t.Name} cleared! +{result.Reward.Tokens} Card Token{(result.Reward.Tokens == 1 ? "" : "s")}");
            if (result.ComboBreaker && Posters.BreakerFor(o.Character) != null)
                r.Unlocks.Add($"Combo Breaker! You stopped {o.Handle}'s combo before its payoff: its poster is yours (the Locker)");
            if (result.Page != null) r.Unlocks.Add($"A new page in the Unlisted binder: {result.Page.Handle} (the Library's shelf)");
            // Every table this clear opened (The Black Market's opens two).
            if (result.Opened.Count > 0) r.Unlocks.AddRange(result.Opened.Select(opened => $"New table: {opened}"));
            else if (result.NightOver) r.Unlocks.Add("The night's over: a new lineup sits down at the table");
            foreach (var up in result.Reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : ""));
            AddGrants(r, result.Reward);
            _scenes.LastResults = r;
            // The scenes' {card}, {table}, {handle}, {grade} come from the engine (AlleyResult.Fill, queued with them); {nick} from the profile.
            void Then(Action then) => PlayResultStory(story, then);
            // After an encore that isn't won, "Try again" is the encore again; otherwise the table's next seat.
            string? again = o.Seat == 0 && !result.SeatWon ? o.Character : null;
            var buttons = new List<(string, Action)>
            {
                ("Continue", () => Then(() => _campaignScreen.Open(result.Summary))),
                (result.SeatWon ? "Next seat" : "Try again", () => Then(() => ShowAlleyVersus(table, again))),
            };
            _scenes.ShowResults(r, () => Then(() => _campaignScreen.Open(result.Summary)), () => Then(() => ShowAlleyVersus(table, again)), () => { }, buttons);
        }
    }
}
