using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Special guests (campaign/guests.txt, Campaign > Guests): duelists outside the circuits, each unlocked by a
    /// milestone. Challenge them one on one, or with a partner against them and theirs (once the first Tag Cup is cleared,
    /// or their own tagunlock rule is met; a guest without a partner has no tag duel). A guest with several decks plays
    /// one a duel, moving on with each win (<see cref="GuestList.DeckFor"/>). A win pays like a match of the
    /// circuit you're on (plus their prize the first time) and counts against them, so they can partner you after.
    /// Guest duels follow the rules of the last circuit you've cleared, or of the one you're on if your deck is ready for it
    /// (a guest unlocked by the Every Era final: the last circuit's, <see cref="GuestRules"/>).
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The guest duel being played: the guest, whether it's a tag duel, and your partner's id in one.</summary>
        private (Guest guest, bool tag, string partner)? _guestDuel;

        /// <param name="tagLockedFor">A guest whose Tag duel was just pressed while it's still locked: their row says why.</param>
        private void ShowGuests(int tagLockedFor = -1)
        {
            var profile = _campaign.Profile;
            if (profile == null) return;
            var guests = _campaign.Guests.All;
            var rows = guests.Select((g, i) =>
            {
                bool open = GuestList.IsUnlocked(g, _campaign.Circuits, profile);
                bool tagOpen = GuestList.IsTagUnlocked(g, _campaign.Circuits, profile);
                int wins = profile.WinsAgainst(g.Character.Id);
                // The prize waits on a first win against them here (their one-on-one key), not anywhere: a guest you also met
                // in the circuits (Vera's rematch) has wins against her long before. A "once" guest's doesn't come back in New Game+.
                bool prizeLeft = GuestList.PrizeDue(g, profile);
                // The tag duel waits for the first Tag Cup (where tag duels are taught and you pick your partner).
                var tagLock = open && !tagOpen
                    ? (i == tagLockedFor ? $" · <color=#E3A21A>Tag duel locked: {GuestList.TagUnlockText(g, _campaign.Circuits, profile)}</color>"
                        : $" · Tag duel: {GuestList.TagUnlockText(g, _campaign.Circuits, profile)}")
                    : "";
                // Several decks, one a duel: the next one (moved on by each win) and where it sits in the turn.
                var next = g.Decks.Count > 1 ? $" · next: {DeckTitle(GuestList.DeckFor(g, profile))} ({GuestList.DeckIndex(g, profile) + 1} of {g.Decks.Count})" : "";
                var decks = g.Decks.Count > 1
                    ? $"Plays {CountWord(g.Decks.Count)} decks, one a duel: {string.Join(", ", g.Decks.Select(DeckTitle))}"
                    : $"Plays {DeckTitle(g.Deck)}";
                return new CampaignScenes.GuestRow
                {
                    Id = g.Character.Id, Name = g.Character.DisplayName, Blurb = g.Blurb, Unlocked = open, TagLocked = open && !tagOpen, HasTag = g.HasTag,
                    Decks = decks + (g.HasTag ? $" · in a tag duel with {g.Partner.DisplayName} ({DeckTitle(g.PartnerDeck)})" : " · no tag duel"),
                    Status = (!open ? $"Locked: {GuestList.UnlockText(g, profile)}"
                        : (!prizeLeft ? $"Beaten {wins}x: they can partner you in tag duels" : $"First win: +{g.Prize:N0} DP") + next) + (g.HasTag ? tagLock : ""),
                };
            }).ToList();
            // A locked tag duel stays on this screen and says what opens it.
            _scenes.ShowGuests(rows, i => GuestVersus(guests[i], false),
                i => { if (GuestList.IsTagUnlocked(guests[i], _campaign.Circuits, profile)) GuestVersus(guests[i], true); else ShowGuests(i); },
                () => _campaignScreen.Open(),
                scout: i => _scenes.ShowScoutReport(GuestScouts(guests[i], profile), () => ShowGuests(), over: true));
        }

        /// <summary>The guests screen's Scout: the deck they play next one on one (only that one), and their partner's in a tag duel.</summary>
        private List<CampaignScenes.Scout> GuestScouts(Guest g, Profile profile) =>
            g.HasTag ? ScoutsFor((g.Character.Name, GuestList.DeckFor(g, profile), g.Character.Id), (g.Partner.Name, g.PartnerDeck, g.Partner.Id))
                : ScoutsFor((g.Character.Name, GuestList.DeckFor(g, profile), g.Character.Id));

        /// <summary>A small count in words ("four decks"), digits past nine.</summary>
        private static string CountWord(int n) =>
            n >= 2 && n <= 9 ? new[] { "two", "three", "four", "five", "six", "seven", "eight", "nine" }[n - 2] : n.ToString();

        /// <summary>
        /// The circuit whose rules guest duels follow: the last one you've cleared (so the deck that won it can play); for a
        /// guest unlocked by the Every Era final, the last circuit (the final comes after it, and her decks are today's: an NG+
        /// save would otherwise meet them under the Beginner list).
        /// </summary>
        private int GuestRules(Profile profile, Guest? guest = null)
        {
            if (guest != null && guest.Unlock.Equals("final", StringComparison.OrdinalIgnoreCase) && _campaign.Circuits.All.Count > 0)
                return _campaign.Circuits.All.Count - 1;
            int last = 0;
            for (int i = 0; i < _campaign.Circuits.All.Count; i++)
                if (_campaign.Circuits.IsCleared(i, profile)) last = i;
            return last;
        }

        /// <summary>Before a guest duel: your side (and partner) against theirs.</summary>
        private void GuestVersus(Guest g, bool tag, bool cycled = false)
        {
            var profile = _campaign.Profile!;
            // A tag duel before its milestone (a rematch, say): back to the guests, saying what opens it.
            if (tag && !GuestList.IsTagUnlocked(g, _campaign.Circuits, profile)) { ShowGuests(_campaign.Guests.All.ToList().IndexOf(g)); return; }
            // A guest's first meeting (guest:<id>): its scene, then this versus (only when the story has one for them).
            var meeting = Story.GuestEvent(g.Character.Id);
            if (!cycled && _story.Pending(profile, new[] { meeting }).Count > 0)
            {
                PlayStory(new[] { meeting }, "", () => GuestVersus(g, tag));
                return;
            }
            // The deck they play one on one this duel (a guest with several moves on with each win); the tag duel's is their first.
            var theirDeck = tag ? g.Deck : GuestList.DeckFor(g, profile);
            // Your deck, as before a circuit match: one of yours or a loaner (half DP). One of yours that's ready for the
            // circuit you're on, or for the last one you cleared, can play (older lists can be the stricter ones); a loaner
            // follows the last one you cleared.
            var choices = PickVersusDeck(cycled);
            int cleared = GuestRules(profile, g), current = _campaign.Circuits.Current(profile);
            List<DeckProblem> Problems(string deck, int c) => _library.Contains(deck)
                ? _campaign.Problems(_library.Load(deck), c) : new List<DeckProblem> { new DeckProblem { Message = "Build a deck first." } };
            int RulesFor(string deck) =>
                _campaign.IsLoanerDeck(deck) || Problems(deck, cleared).Count == 0 || Problems(deck, current).Count > 0 ? cleared : current;
            string notReady = "";
            if (!_campaign.IsLoanerDeck(_versusDeck!) && Problems(_versusDeck!, RulesFor(_versusDeck!)) is { Count: > 0 } problems)
            {
                // Not ready: borrow a deck instead when there's one to borrow (the arrows still reach yours).
                var loaner = choices.FirstOrDefault(_campaign.IsLoanerDeck);
                if (loaner == null)
                {
                    _campaignScreen.Open($"<color=#FF8A8A>Your deck isn't ready for a guest duel</color> (they follow {_campaign.Circuits.All[cleared].Name}'s rules{(current != cleared ? $", or {_campaign.Circuits.All[current].Name}'s" : "")}): {problems[0].Message}");
                    return;
                }
                notReady = $"\n<color=#E3A21A>{DeckTitle(_versusDeck!)} isn't ready for these rules: {problems[0].Message}</color>";
                if (!cycled) { _versusDeck = loaner; notReady = $"\n<color=#E3A21A>Your deck isn't ready for these rules ({problems[0].Message}): borrowing one.</color>"; }
            }
            int circuit = RulesFor(_versusDeck!);
            bool ready = _campaign.IsLoanerDeck(_versusDeck!) || Problems(_versusDeck!, circuit).Count == 0;
            var mine = YourVersusDeck(circuit);
            Deck Load(string name) => _library.Contains(name) ? _library.Load(name) : new Deck();
            var them = new CampaignScenes.Side
            {
                Name = tag ? $"{g.Character.Name} & {g.Partner.Name}" : g.Character.Name, Title = tag ? "" : g.Character.Title,
                Deck = tag ? $"{DeckTitle(g.Deck)} + {DeckTitle(g.PartnerDeck)}" : DeckTitle(theirDeck),
                Showcase = tag ? TeamShowcase(Load(g.Deck), Load(g.PartnerDeck)) : CampaignScenes.ShowcaseOf(Load(theirDeck), _cardPack!).Take(3).ToList(),
                Portraits = tag ? new List<string> { g.Character.Id, g.Partner.Id } : new List<string> { g.Character.Id },
                Detail = g.Blurb, Quote = g.Character.Intro,
            };
            // Start: only with a deck that can play (the arrows may have landed on one of yours that isn't ready).
            void Start(Action duel)
            {
                if (!ready) { GuestVersus(g, tag, cycled: true); return; }
                seed++;
                duel();
            }
            Action<int> cycle = delta => { CycleVersusDeck(choices, delta); GuestVersus(g, tag, cycled: true); };
            if (!tag)
            {
                var you = new CampaignScenes.Side
                {
                    Name = "You", Deck = VersusDeckName(_versusDeck!), DeckNote = VersusDeckNote(),
                    Showcase = CampaignScenes.ShowcaseOf(mine, _cardPack!).Take(3).ToList(),
                    Detail = $"Guest duel. Rules: {_campaign.Circuits.All[circuit].Name}." + LoanerAdjustments(circuit) + notReady,
                };
                BossSting(BossMusic.ForGuest(g), cycled);
                _scenes.ShowVersus("Special guest", g.Character.DisplayName, you, them,
                    start: () => Start(() =>
                    {
                        BeginLiveDuel(LiveDuels.Guest, guest: g.Character.Id);
                        CoinToss("You", g.Character.Name, _versusDeck!, theirDeck, () => StartGuestDuel(g, false, "", circuit), measured: MeasuredToss(GuestList.Key(g, false)));
                    }),
                    back: () => ShowGuests(), cycleDeck: cycle,
                    scouts: ScoutsFor((g.Character.Name, theirDeck, g.Character.Id)), stuck: Stuck(GuestList.Key(g, false), false, new[] { g.Character.Id }, theirDeck), quiet: cycled,
                    texts: _campaign.TextsLine(mine, _campaign.Rules(circuit)));
                return;
            }
            // Your partner: the one you picked last on a Tag Cup's screen (anyone you've beaten, or Kit).
            var partners = _campaign.TagPartnerChoices().Where(p => p.Id != g.Character.Id && p.Id != g.Partner.Id).ToList();
            var partner = partners.FirstOrDefault(p => p.Id == profile.TagPartner) ?? partners.FirstOrDefault();
            if (partner == null) { _campaignScreen.Open("<color=#FF8A8A>You need a partner:</color> beat a duelist in the circuits first."); return; }
            var partnerDeck = _campaign.PartnerDeck(partner);
            var team = new CampaignScenes.Side
            {
                Name = $"You & {partner.Name}", Title = partner.Title.Length > 0 ? $"with {partner.DisplayName}" : "",
                Deck = $"{VersusDeckName(_versusDeck!)} + {DeckTitle(partnerDeck)}", DeckNote = VersusDeckNote(partnerDeck), Showcase = TeamShowcase(mine, PartnerDeckFor(partnerDeck, circuit)),
                Portraits = { partner.Id }, Detail = "Tag duel: one field and 8,000 LP per team. Your partner is the one you last picked on a Tag Cup's screen (Campaign, then a Tag Cup)." + LoanerAdjustments(circuit) + notReady,
                Quote = partner.Intro,
            };
            BossSting(BossMusic.ForGuest(g), cycled);
            _scenes.ShowVersus("Special guest · Tag duel", $"You & {partner.Name} against {g.Character.Name} & {g.Partner.Name}", team, them,
                start: () => Start(() =>
                {
                    BeginLiveDuel(LiveDuels.Guest, guest: g.Character.Id, tag: true, partnerDeck: partnerDeck);
                    CoinToss($"You & {partner.Name}", $"{g.Character.Name} & {g.Partner.Name}", _versusDeck!, g.Deck, () => StartGuestDuel(g, true, partner.Id, circuit),
                        tag: true, measured: MeasuredToss(GuestList.Key(g, true), partner.Id, partnerDeck));
                }),
                back: () => ShowGuests(), cycleDeck: cycle,
                scouts: ScoutsFor((g.Character.Name, g.Deck, g.Character.Id), (g.Partner.Name, g.PartnerDeck, g.Partner.Id)),
                stuck: Stuck(GuestList.Key(g, true), true, new[] { g.Character.Id, g.Partner.Id }, g.Deck, g.PartnerDeck), quiet: cycled,
                texts: _campaign.TextsLine(mine, _campaign.Rules(circuit), partnerDeck));
        }

        private void StartGuestDuel(Guest g, bool tag, string partnerId, int circuit)
        {
            var profile = _campaign.Profile!;
            _loanerCircuit = circuit;
            var yours = TakeVersusDeck();
            _paused = false;
            int Index(Personality p) => Math.Max(0, _roster.FindIndex(r => r.Id == p.Id));
            string DeckOf(Personality p, string deck) => _allDecks.Contains(deck) ? deck : OwnDecks(p)[0];
            if (!tag)
            {
                _pickCharacter[0] = -1;
                _pickDeck[0] = yours;
                _pickCharacter[1] = Index(g.Character);
                _pickDeck[1] = DeckOf(g.Character, GuestList.DeckFor(g, profile));
                ShowPicks();
                NewDuel(guestDuel: (g, false, ""));
            }
            else
            {
                var partner = _roster[Index(_roster.First(p => p.Id == partnerId))];
                var lineup = new TagLineup
                {
                    Characters = new[] { -1, Index(partner), Index(g.Character), Index(g.Partner) },
                    Decks = new[] { yours, _campaign.PartnerDeck(partner), DeckOf(g.Character, g.Deck), DeckOf(g.Partner, g.PartnerDeck) },
                    PartnerStyle = TagPartners.Playing(partner, profile),
                };
                NewDuel(null, lineup, guestDuel: (g, true, partnerId));
            }
            UpdateControls();
        }

        /// <summary>Whether you played the guest duel with a loaner (half DP).</summary>
        private bool GuestLoaner(bool tag) => TeamHasLoaner(tag ? _tag : null, _pickDeck[0]);

        /// <summary>At the end of a guest duel: records it and shows the results. False when this isn't one.</summary>
        private bool FinishGuestDuel()
        {
            if (_guestDuel is not { } gd || _watching != null || _campaign.Profile == null) return false;
            int winner = _board.Winner > 1 ? 2 : _board.Winner;
            var reward = GuestList.Record(_campaign.Profile, _campaign.Circuits, gd.guest, gd.tag, winner, loaner: GuestLoaner(gd.tag));
            _campaign.EndLiveDuel();
            _campaign.AfterDuel(reward, "", _tally, GuestLoaner(gd.tag), gd.tag);
            _campaign.Save();
            BeginGuestResults(gd, reward, winner);
            var them = gd.tag ? $"{gd.guest.Character.Name} & {gd.guest.Partner.Name}" : gd.guest.Character.Name;
            var line = winner == 0 ? gd.guest.Character.LoseLine : winner == 1 ? gd.guest.Character.WinLine : "";
            // A Yata-lock ending says who's locked (you, the guest, or a team), as a round's or a cup's result does.
            if (_runner?.Result.Reason == DuelRunner.ReasonYataLock && winner is 0 or 1) line = YataLockLine(them, !gd.tag ? "" : _partners.Length > 0 ? _partners[0].Name : "your partner");
            _view.SetResult(winner == 2 ? "Draw!" : winner == 0 ? "You win!" : $"{them} win{(gd.tag ? "" : "s")}!", line, $"{reward.Summary}   ·   {_campaign.Profile.Dp:N0} DP");
            _view.AppendLog($"Campaign: {reward.Summary}.");
            return true;
        }

        /// <summary>After a guest duel: its scenes (its card unlocks') queued, then the results in 2 s (DuelController.Results).</summary>
        private void BeginGuestResults((Guest guest, bool tag, string partner) gd, DuelReward reward, int winner)
        {
            // The save's first loss (Gus's first-loss scene), then the card unlocks' scenes.
            var events = Story.LossEvents(_campaign.Profile!, winner);
            AddGrantEvents(events, reward);
            // The guest's own win (guestbeat:<id>, a first one-on-one win), after the gifts' scenes.
            events.AddRange(Story.GuestEvents(gd.guest, gd.tag, winner, reward));
            ShowResultsSoon(events, reward.Grants, story => ShowGuestResults(gd, reward, winner, story));
        }

        private void ShowGuestResults((Guest guest, bool tag, string partner) gd, DuelReward reward, int winner, List<CampaignState.QueuedStory> story)
        {
            var g = gd.guest;
            var profile = _campaign.Profile!;
            var r = new CampaignScenes.Results
            {
                Won = winner == 0,
                Headline = winner > 1 ? "Draw" : winner == 0 ? "Victory!" : "Defeat",
                Speaker = g.Character.Name,
                Quote = winner == 0 ? g.Character.LoseLine : winner == 1 ? g.Character.WinLine : "",
                Circuit = gd.tag ? $"Special guest · Tag duel against {g.Character.Name} & {g.Partner.Name}" : $"Special guest · {g.Character.DisplayName}",
                TotalDp = profile.Dp,
            };
            if (winner == 0)
            {
                r.Earnings.Add((GuestLoaner(gd.tag) ? "Win (half: loaner deck)" : "Win", reward.Dp - (reward.FirstWin ? g.Prize : 0)));
                if (reward.FirstWin) r.Earnings.Add(($"First win against {g.Character.Name}", g.Prize));
            }
            else r.Earnings.Add((winner == 1 ? "For the duel" : "Draw", reward.Dp));
            foreach (var up in reward.LevelUps.Where(u => u.Dp > 0)) r.Earnings.Add(($"Level {up.Level} reward", up.Dp));
            var (into, needed) = Leveling.Progress(profile.Exp);
            r.Exp = (reward.Exp, profile.Level, into, needed);
            int wins = profile.WinsAgainst(g.Character.Id), losses = profile.Losses.TryGetValue(g.Character.Id, out var l) ? l : 0;
            r.Record = $"Against {g.Character.Name}: <b>{wins}</b> win{(wins == 1 ? "" : "s")}, <b>{losses}</b> loss{(losses == 1 ? "" : "es")}";
            foreach (var up in reward.LevelUps)
                r.Unlocks.Add($"Level {up.Level}!" + (up.Tokens > 0 ? $" +{up.Tokens} Card Token{(up.Tokens > 1 ? "s" : "")}" : ""));
            if (winner == 0 && wins == 1) r.Unlocks.Add($"{g.Character.Name} can partner you in tag duels now");
            AddGrants(r, reward);
            _scenes.LastResults = r;
            _scenes.ShowResults(r,
                continueAction: () => PlayResultStory(story, () => ShowGuests()),
                rematch: Requeued(story, () => GuestVersus(g, gd.tag)),
                shop: Requeued(story, () => _shop.Open()));
        }
    }
}
