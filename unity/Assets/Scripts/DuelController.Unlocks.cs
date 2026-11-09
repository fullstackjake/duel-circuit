using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Card unlocks in the game (CARD-UNLOCKS-SPEC-2026-11-01): the duel's tally for Gus's bounties, each result's grants
    /// on its results screen and in its story, and the -campaigncheck steps for them.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>What happened in the duel being played, for the bounty board (null in a replay).</summary>
        private DuelTally? _tally;

        /// <summary>A result's card unlocks as results rows ("Vera's gift: Gearfried the Swordmaster · Raw · Near Mint").</summary>
        private static void AddGrants(CampaignScenes.Results r, DuelReward reward)
        {
            foreach (var g in reward.Grants) r.Unlocks.Add(g.Text);
        }

        /// <summary>The story events a result's card unlocks raise ("beat:vera", "bond:vera:3", "setcomplete"...).</summary>
        private static void AddGrantEvents(List<string> events, DuelReward reward)
        {
            foreach (var e in CardUnlocks.Events(reward.Grants))
                if (!events.Contains(e)) events.Add(e);
        }

        // ---- -campaigncheck --------------------------------------------------------------------------

        /// <summary>
        /// A first circuit win's rival gift (called by the check after it records <paramref name="reward"/>, a win against
        /// <paramref name="rival"/>, and shows its results): on the results screen, and in the collection as a grade 8
        /// reward copy that sells for 0.
        /// </summary>
        private void CheckRivalGift(Action<string, bool> report, Profile profile, DuelReward reward, string rival)
        {
            var gift = reward.Grants.FirstOrDefault(g => g.Kind == GrantKind.RivalGift && g.Character == rival);
            var news = _scenes.LastResults?.Unlocks ?? new List<string>();
            if (_campaign.Unlocks.SignatureOf(rival) == 0) { report($"{rival} has no signature card, so no gift to check", true); return; }
            if (gift == null) { report($"the first win against {rival} pays their gift (grants: {CampaignState.GrantsLine(reward.Grants)})", false); return; }
            var copy = gift.Copy;
            report($"the first win against {rival} shows their gift on the results screen ({gift.Text})", news.Contains(gift.Text));
            report($"the gift is in the collection: grade {copy?.Grade}, a reward copy, sells for {(copy == null ? -1 : profile.Collection.SellPrice(copy))} DP",
                copy != null && profile.Collection.CopiesOf(gift.Code).Contains(copy) && copy.Grade == Grades.Fresh && copy.Source == CopySource.Reward
                && profile.Collection.SellPrice(copy) == 0);
        }

        /// <summary>
        /// The bounty board (3 rows for the band), a set completion's poster in the Locker, and the bond trade dialog. The
        /// state each step changes is put back.
        /// </summary>
        private System.Collections.IEnumerator UnlocksCheck(Action<string, bool> report, Func<string, System.Collections.IEnumerator> shot)
        {
            var profile = _campaign.Profile!;
            var unlocks = _campaign.Unlocks;
            int cupBefore = _campaignScreen.DebugSelectedCup;

            // Gus's board isn't up before he introduces it: with the Beginner Circuit not cleared, his scene not seen and no
            // New Game+, the campaign screen has no Bounties button and the board shows nothing (BountyBoard.IsOpen).
            {
                var winsBefore = new Dictionary<string, int>(profile.MatchWins);
                var seenGate = new HashSet<string>(profile.Seen);
                int cycleBefore = profile.Cycle;
                int gate = BountyBoard.GateCircuit(_campaign.Circuits);
                if (gate >= 0)
                    for (int m = 0; m < _campaign.Circuits.All[gate].Matches.Count; m++) profile.MatchWins.Remove(Profile.MatchKey(gate, m));
                profile.Seen.Remove(BountyBoard.IntroScene);
                profile.Cycle = 0;
                _campaignScreen.Open();
                bool buttonShut = !_campaignScreen.DebugBountiesButton;
                int shutRows = _campaignScreen.DebugOpenBounties();
                report($"before Gus puts the board up ({BountyBoard.OpensAfter} not cleared, {BountyBoard.IntroScene} not seen) there's no Bounties button and no board ({shutRows} rows)",
                    gate >= 0 && buttonShut && shutRows == 0 && !_campaignScreen.DebugBountiesOpen && !unlocks.BountiesOpen(profile));
                _campaignScreen.DebugCloseBounties();
                profile.MatchWins.Clear();
                foreach (var p in winsBefore) profile.MatchWins[p.Key] = p.Value;
                profile.Seen.Clear();
                foreach (var k in seenGate) profile.Seen.Add(k);
                profile.Cycle = cycleBefore;
                _campaignScreen.Close();
            }

            // Gus's bounty board, from the Bounties button beside Lessons (up once his scene is seen; put back after).
            bool introAdded = profile.Seen.Add(BountyBoard.IntroScene);
            _campaignScreen.Open();
            bool buttonShown = _campaignScreen.DebugBountiesButton;
            int rows = _campaignScreen.DebugOpenBounties();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return shot("bounty-board");
            report($"the bounty board opens with the {unlocks.BoardBand(profile)} band's {rows} rows ({unlocks.OpenBounties(profile)} open; {unlocks.BountyPays(profile)})",
                rows == 3 && _campaignScreen.DebugBountiesOpen && buttonShown);
            _campaignScreen.DebugCloseBounties();
            if (introAdded) profile.Seen.Remove(BountyBoard.IntroScene);

            // A set completed: its cards granted here, then CheckSets pays its framed card and the poster joins the Locker.
            var set = _campaign.Shop?.Sets.Where(s => !profile.SetsDone.ContainsKey(s.Info.Code))
                .OrderBy(s => s.Pool.Values.SelectMany(c => c).Distinct().Count(c => !profile.Collection.Owns(c))).FirstOrDefault();
            if (set == null) report("a shop set to complete for the poster check", false);
            else
            {
                var copiesBefore = profile.Collection.All.ToList();
                var dpBefore = SnapshotDp(profile);
                int tokensBefore = profile.Tokens;
                string posterBefore = profile.Poster;
                var setsBefore = new HashSet<string>(profile.SetsDone.Keys);
                var seenBefore = new HashSet<string>(profile.Seen);
                foreach (var pair in set.Pool)
                    foreach (var code in pair.Value.Distinct())
                        if (!profile.Collection.Owns(code))
                        {
                            // A gift's printing (spec 4.2): the newest at that rarity by the band's era year, as CardUnlocks stamps its gifts.
                            var gift = profile.Collection.NewCopy(code, pair.Key, CopySource.Reward);
                            if (_campaign.Printings is PrintingIndex printings)
                                profile.Collection.Stamp(gift, CopyPrints.Shop(code, pair.Key, _campaign.Circuits.BandEra(profile), printings, _cardPack), printings);
                            profile.Collection.Take(gift);
                        }
                var line = _campaign.CheckSets();
                var posterId = Posters.SetPrefix + set.Info.Code;
                int earned = _campaignScreen.DebugOpenLocker();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return shot("locker-set-poster");
                var poster = Posters.Find(posterId, profile);
                report($"completing {set.Info.Name} pays and earns its poster ({line}); the Locker lists it ({earned} earned)",
                    line.Contains(set.Info.Name) && poster != null && Posters.IsUnlocked(poster, _campaign.Circuits, profile)
                    && _campaignScreen.DebugLockerShows(posterId) && Posters.AllFor(profile).Any(p => p.Id == posterId));
                report($"the set's poster can be hung on your plate ({poster?.Title})",
                    _campaignScreen.DebugPickPoster(posterId) && Posters.Current(_campaign.Circuits, profile)?.Id == posterId);
                _campaignScreen.DebugCloseLocker();
                // Put it all back: the copies granted and paid, the set, the poster, DP and tokens.
                var keep = new HashSet<CardCopy>(copiesBefore);
                foreach (var c in profile.Collection.All.Where(c => !keep.Contains(c)).ToList()) profile.Collection.Remove(c);
                var now = new HashSet<CardCopy>(profile.Collection.All);
                foreach (var c in copiesBefore.Where(c => !now.Contains(c))) profile.Collection.Insert(c);
                foreach (var done in profile.SetsDone.Keys.Where(k => !setsBefore.Contains(k)).ToList()) profile.SetsDone.Remove(done);
                profile.Poster = posterBefore;
                profile.Seen.Clear();
                foreach (var k in seenBefore) profile.Seen.Add(k);
                RestoreDp(profile, dpBefore);
                profile.Tokens = tokensBefore;
                _campaign.Save();
            }

            // A bond 3 trade on offer: the Trade button by the partner picker opens the dialog (what they give, your spare).
            var partner = _campaign.TagPartnerChoices().FirstOrDefault(p => !TagPartners.IsShopRegular(p) && unlocks.DecksOf(p.Id).Count > 0);
            if (partner == null || _campaign.Circuits.TagCups.Count == 0) report("a partner with decks for the bond trade check", false);
            else
            {
                string partnerBefore = profile.TagPartner;
                bool offered = profile.Unlocks.Add("bondtrade:" + partner.Id);
                _campaignScreen.DebugShowTagCup(0);
                _campaignScreen.DebugPickPartner(partner.Id);
                var (opened, offers, gives) = _campaignScreen.DebugOpenTrade();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return shot("bond-trade");
                report($"with {partner.Name}'s bond 3 trade on offer, Trade opens the dialog ({offers} cards they give, {gives} spares of yours for the first)",
                    unlocks.CanTrade(profile, partner.Id) && opened && offers == unlocks.TradeOffers(profile, partner.Id).Count);
                _campaignScreen.DebugCloseTrade();
                if (offered) profile.Unlocks.Remove("bondtrade:" + partner.Id);
                profile.TagPartner = partnerBefore;
                _campaign.Save();
            }
            _campaignScreen.DebugSelectedCup = cupBefore;
            _campaignScreen.Close();
        }
    }
}
