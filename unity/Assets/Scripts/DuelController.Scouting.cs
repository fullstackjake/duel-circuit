using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Scouting on the versus screen (a report on each opposing deck: its plan, the cards to watch, what beats it, the turn
    /// to want) and, after <see cref="StuckAfter"/> losses in a row at a match, the "Stuck?" advice on top of it: the tools
    /// that change a hard match: what did best when that match was measured (campaign/wall-advice.txt), else a stronger deck
    /// to borrow and a stronger partner, and the turn to take. The CPUs stay as they are.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Losses in a row at a match before the versus screen opens its advice by itself.</summary>
        public const int StuckAfter = 3;

        /// <summary>A reason from campaign/scouting.txt as a sentence ("with the strongest decks..." → "With the strongest decks....").</summary>
        private static string Sentence(string why) =>
            why.Length == 0 ? "" : char.ToUpperInvariant(why[0]) + why.Substring(1) + (why.EndsWith(".") ? "" : ".");

        /// <summary>The scouting reports for the decks you're about to face (who plays each, and the deck).</summary>
        private List<CampaignScenes.Scout> Scouts(params (string who, string deck)[] them) =>
            ScoutsFor(them.Select(t => (t.who, t.deck, "")).ToArray());

        /// <summary>The same, with the character playing each deck (a report written for one character reaches only them).</summary>
        private List<CampaignScenes.Scout> ScoutsFor(params (string who, string deck, string character)[] them) =>
            them.Select(t =>
            {
                var r = _campaign.Scout(t.deck, t.character);
                return new CampaignScenes.Scout
                {
                    Who = t.who, Deck = DeckTitle(t.deck), Plan = r.Plan, Keys = r.Keys, Counters = r.Counters,
                    Build = r.Build.Select(b => _campaign.Owned(b) is int n && n > 0 ? $"{b} (you have {n})" : $"{b} (none yet)").ToList(),
                    Turn = r.Turn == "first" ? $"go first. {Sentence(r.TurnWhy)}" : r.Turn == "second" ? $"go second. {Sentence(r.TurnWhy)}" : "",
                };
            }).ToList();

        /// <summary>A measured option you can take: the deck to pick on the versus screen, and the partner and their deck.</summary>
        public sealed class MeasuredPick
        {
            public MeasuredOption Option = null!;
            /// <summary>Your deck to pick: your own (<see cref="Profile.Deck"/>) or a loaner name ("loaner:...").</summary>
            public string Deck = "";
            public Personality? Partner;
            public string PartnerDeck = "";
        }

        /// <summary>
        /// The measured options at <paramref name="matchKey"/> (campaign/wall-advice.txt) that you can take, best first: your
        /// own deck or a loaner you've unlocked; in a tag duel a partner you've beaten who isn't in this match, with a deck
        /// they can bring (Kit: a recipe you've unlocked).
        /// </summary>
        private List<MeasuredPick> MeasuredPicks(string matchKey, bool tag, params string[] theirIds) => MeasuredPicks(_campaign.Advice.For(matchKey), tag, theirIds);

        /// <summary>The same from a match's measured options (a Library cup match's: campaign/library-advice.txt).</summary>
        private List<MeasuredPick> MeasuredPicks(IEnumerable<MeasuredOption> measured, bool tag, params string[] theirIds)
        {
            var profile = _campaign.Profile!;
            var loaners = _campaign.LoanerDecks();
            var partners = tag ? _campaign.TagPartnerChoices().Where(p => !theirIds.Contains(p.Id)).ToList() : new List<Personality>();
            var picks = new List<MeasuredPick>();
            foreach (var o in measured)
            {
                var deck = o.Borrows ? Loaners.NameFor(o.Deck) : profile.Deck;
                if (o.Borrows && !loaners.Contains(deck)) continue;
                if (o.Partner.Length > 0 != tag) continue;
                var pick = new MeasuredPick { Option = o, Deck = deck };
                if (tag)
                {
                    pick.Partner = partners.FirstOrDefault(p => p.Id == o.Partner);
                    if (pick.Partner == null) continue;
                    var choices = _campaign.PartnerDeckChoices(pick.Partner);
                    pick.PartnerDeck = choices.FirstOrDefault(d => d == o.PartnerDeck || d == Loaners.NameFor(o.PartnerDeck)) ?? "";
                    if (pick.PartnerDeck.Length == 0) continue;
                }
                picks.Add(pick);
            }
            return picks;
        }

        /// <summary>A measured option in words: "a Fire King Snake-Eye loaner, with Teodoro Brasa bringing Fire King Snake-Eye".</summary>
        private string Describe(MeasuredPick pick)
        {
            var title = DeckTitle(pick.Option.Deck);
            var deck = pick.Option.Borrows ? $"{("AEIOU".IndexOf(char.ToUpperInvariant(title.FirstOrDefault())) >= 0 ? "an" : "a")} {title} loaner" : "your own deck";
            return pick.Partner == null ? deck : $"{deck}, with {pick.Partner.Name} bringing {DeckTitle(Loaners.RecipeOf(pick.PartnerDeck))}";
        }

        /// <summary>Most stand-ins (cards not sold yet) an owned copy may have and still be offered as the measured deck.</summary>
        private const int MaxStandIns = 5;

        /// <summary>What the last Stuck? purchase did, for the versus screen it redraws (shown once).</summary>
        private string _ownedNote = "";

        /// <summary>
        /// The Stuck? panel's purchase: own the deck its first tip names, when that's a loaner (what was measured at this
        /// match). Buys the cards you're missing from the loaner's list here and makes it your campaign deck, full DP and
        /// yours to edit; one you've already built is just picked. Null when there's nothing to offer.
        /// <paramref name="redraw"/> shows the versus screen again with the new deck picked.
        /// </summary>
        private CampaignScenes.StuckOffer? OwnOffer(string matchKey, bool tag, string[] theirIds, int circuit, Action redraw) =>
            OwnOffer(matchKey, _campaign.Advice.For(matchKey), tag, theirIds, _campaign.Rules(circuit), redraw);

        /// <summary>
        /// The same for any match: <paramref name="streakKey"/> keeps its losses in a row, <paramref name="measured"/> is
        /// what was measured there, and the loaner is priced and built under <paramref name="rules"/> (a Library cup's).
        /// </summary>
        /// <param name="unmeasured">
        /// With nothing measured here, the loaner to offer instead (the alley's brackets: the alley doesn't lend, so the
        /// strongest recipe you've unlocked, <see cref="StrongerRecipe"/>, built as your own under its list).
        /// </param>
        private CampaignScenes.StuckOffer? OwnOffer(string streakKey, IEnumerable<MeasuredOption> measured, bool tag, string[] theirIds, FormatRules rules, Action redraw,
            bool now = false, string? unmeasured = null)
        {
            var profile = _campaign.Profile!;
            // `now`: offered at once (a final round none of your decks can play), not after losses in a row.
            if (!now && profile.LossesInARow(streakKey) < StuckAfter) return null;
            var pick = MeasuredPicks(measured, tag, theirIds).FirstOrDefault();
            string? own = pick != null ? (pick.Option.Borrows ? pick.Deck : null) : unmeasured;
            if (own == null) return null;
            var quote = _campaign.QuoteOwn(own, rules);
            if (quote == null || quote.StandIns > MaxStandIns) return null;
            void Picked(string deck, string note)
            {
                _versusDeck = deck;
                _ownedNote = note;
                Sounds.Play("level_up", 0.6f);
                redraw();
            }
            var title = DeckTitle(quote.Recipe);
            // Nothing measured (the alley's brackets): nothing's lent there and its duels pay no DP, so the deck is simply yours.
            bool measuredHere = pick != null;
            string Why = measuredHere ? "Wins pay full DP and you can change it; a loaner pays half." : "It's yours to play here and everywhere, and to change.";
            string Paid = measuredHere ? ": wins pay full DP." : ".";
            if (quote.Existing.Length > 0 && quote.Existing == _versusDeck) return null; // already playing your copy
            if (quote.Existing.Length > 0)
                return new CampaignScenes.StuckOffer
                {
                    Label = "Play your own copy", Note = $"You built <b>{quote.Existing}</b> from this recipe: play it as your own deck. {Why}",
                    Act = () => Picked(quote.Existing, measuredHere ? $"<b>{quote.Existing}</b> is picked: your own deck, so wins pay full DP." : $"<b>{quote.Existing}</b> is picked: your own deck."),
                };
            var standIns = quote.StandIns > 0
                ? $"\n<color=#E3A21A>{quote.StandIns} card{(quote.StandIns == 1 ? " isn't" : "s aren't")} in the shop yet, so cards you own stand in.{(measuredHere ? " It was measured with the full deck." : "")}</color>"
                : "";
            // In a tag duel the measurement was with a partner too; owning the deck doesn't change yours.
            if (tag && pick?.Partner != null) standIns += $"\nMeasured with {pick.Partner.Name} as your partner.";
            int price = quote.Price, cards = quote.Cards;
            var what = price > 0 ? $"Buy the {cards} card{(cards == 1 ? "" : "s")} you're missing and play <b>{title}</b> as your own deck."
                : $"Build <b>{title}</b> from your cards as your own deck.";
            return new CampaignScenes.StuckOffer
            {
                Label = price > 0 ? $"Make it yours · {price:N0} DP" : "Make it yours · free",
                Note = (price > profile.Dp ? $"{what} You have {profile.Dp:N0} DP{(measuredHere ? ": win with the loaner first, then come back." : ".")}" : $"{what} {Why} You have {profile.Dp:N0} DP.") + standIns,
                Enabled = price <= profile.Dp,
                Refused = $"<color=#FF8A8A>Not enough DP:</color> it costs {price:N0} DP, you have {profile.Dp:N0}." + (measuredHere ? " The loaner still works, at half DP." : ""),
                Confirm = price > 0 ? $"Confirm · {price:N0} DP" : "",
                ConfirmNote = $"Buys {cards} card{(cards == 1 ? "" : "s")} and makes <b>{title} (recipe)</b> your campaign deck. Click again to confirm, or Close to cancel.",
                Act = () =>
                {
                    var name = _campaign.Own(quote, rules);
                    if (name == null) return;
                    Picked(name, price > 0
                        ? $"Bought {cards} card{(cards == 1 ? "" : "s")} ({price:N0} DP) and built <b>{name}</b>, now your campaign deck{Paid}"
                        : $"Built <b>{name}</b> from your cards, now your campaign deck{Paid}");
                },
            };
        }

        /// <summary>
        /// After a run of losses at <paramref name="matchKey"/>: what to try, from what you have (null before then). First what
        /// did best when this very match was measured (the wall study); without a measurement, the strongest decks you can
        /// borrow and, in a tag duel, the partners with the strongest decks.
        /// </summary>
        private List<string>? Stuck(string matchKey, bool tag, string[] theirIds, params string[] theirDecks) =>
            Stuck(matchKey, _campaign.Advice.For(matchKey), tag, theirIds, theirDecks);

        /// <summary>The same for any match: <paramref name="streakKey"/> keeps its losses in a row, <paramref name="options"/> is what was measured there.</summary>
        private List<string>? Stuck(string streakKey, IEnumerable<MeasuredOption> options, bool tag, string[] theirIds, params string[] theirDecks) =>
            Stuck(streakKey, options, tag, theirIds, true, theirDecks);

        /// <summary>
        /// The same where <paramref name="lends"/> is false (the alley's brackets: your own decks only): no loaner to borrow,
        /// so the stronger recipes are named as decks to build (Make it yours, <see cref="StrongerRecipe"/>).
        /// </summary>
        private List<string>? Stuck(string streakKey, IEnumerable<MeasuredOption> options, bool tag, string[] theirIds, bool lends, params string[] theirDecks)
        {
            var profile = _campaign.Profile!;
            int lost = profile.LossesInARow(streakKey);
            if (lost < StuckAfter) return null;
            var tips = new List<string> { $"{lost} losses in a row here. Before you try again, one of these will help more:" };
            var measured = MeasuredPicks(options, tag, theirIds);
            // Where the partner is picked: on a Tag Cup's screen (a guest tag duel has none of its own and brings the one
            // you picked last there). Null when no Tag Cup is open yet, so the advice doesn't send you to one.
            var (pickWhere, pickNote) = tag ? PartnerPickedWhere(streakKey.StartsWith("guesttag:")) : (null, "");
            if (measured.Count > 0)
            {
                var best = measured[0].Option;
                tips.Add($"What did best here when it was measured: {Describe(measured[0])} (won about {Math.Max(1, (best.Percent + 5) / 10)} in 10)."
                         + (!tag ? " Pick it with the arrows under your deck."
                            : pickWhere != null ? $" Pick the partner and their deck {pickWhere}, your deck with the arrows here.{pickNote}"
                            : $" Pick your deck with the arrows here. {pickNote}"));
                if (measured.Count > 1) tips.Add($"Next best: {Describe(measured[1])} (about {Math.Max(1, (measured[1].Option.Percent + 5) / 10)} in 10).");
                if (best.YouFirst >= 0 && best.YouSecond >= 0 && Math.Abs(best.YouFirst - best.YouSecond) >= 25)
                    tips.Add($"If you win the toss, go {(best.YouFirst > best.YouSecond ? "first" : "second")}: it won {Math.Max(best.YouFirst, best.YouSecond)}% that way, {Math.Min(best.YouFirst, best.YouSecond)}% the other.");
            }
            else
            {
                // In a tag duel the partner decides the most (measured at the Tag Cup walls: the same deck went from about 1 win
                // in 16 with a weak partner to 14 in 16 with Kit and a strong loaner), so that comes first.
                if (tag && pickWhere == null) tips.Add(pickNote);
                else if (tag)
                {
                    var kitDeck = _campaign.LoanerDecks().OrderByDescending(_campaign.Rating).FirstOrDefault();
                    if (kitDeck != null)
                        tips.Add($"Pick Kit, the shop regular, as your partner {pickWhere} and give Kit a strong recipe like {DeckTitle(Loaners.RecipeOf(kitDeck))}. Kit plays it at full strength.{pickNote}");
                    var partners = _campaign.TagPartnerChoices().Where(p => !TagPartners.IsShopRegular(p) && !theirIds.Contains(p.Id))
                        .Select(p => (p, deck: _campaign.PartnerDeck(p))).Where(x => x.deck.Length > 0)
                        .OrderByDescending(x => _campaign.Rating(x.deck)).Take(2).ToList();
                    if (partners.Count > 0)
                        tips.Add($"Or partner with someone strong: {string.Join(" or ", partners.Select(x => $"{x.p.Name} ({DeckTitle(x.deck)})"))}.");
                }
                double yours = _campaign.Rating(_versusDeck ?? profile.Deck);
                var loaners = _campaign.LoanerDecks().Where(l => _campaign.Rating(l) > yours).OrderByDescending(_campaign.Rating).Take(2).ToList();
                if (loaners.Count > 0 && lends)
                    tips.Add($"Borrow a stronger deck with the arrows under yours: {string.Join(" or ", loaners.Select(l => DeckTitle(Loaners.RecipeOf(l))))}. A loaner win pays half DP.");
                else if (loaners.Count > 0)
                    tips.Add($"Nothing's lent here, but a stronger recipe can be your own deck: {string.Join(" or ", loaners.Select(l => DeckTitle(Loaners.RecipeOf(l))))}. "
                             + "Make it yours buys the cards you're missing (or build it in the Recipe Book).");
            }
            var turns = theirDecks.Select((d, i) => _campaign.Scout(d, i < theirIds.Length ? theirIds[i] : "")).Where(r => r.Turn.Length > 0).ToList();
            foreach (var r in turns.Take(1))
                if (!tips.Any(t => t.StartsWith("If you win the toss"))) tips.Add($"If you win the toss, {(r.Turn == "first" ? "go first" : "go second")}: {r.TurnWhy}.");
            tips.Add("Add answers to the cards each report below says to watch for. Hand traps and removal decide these duels.");
            return tips;
        }

        /// <summary>
        /// The strongest recipe you've unlocked, rated above the deck you've picked (the advice's "stronger deck"), as a
        /// loaner name; null when none is.
        /// </summary>
        private string? StrongerRecipe()
        {
            double yours = _campaign.Rating(_versusDeck ?? _campaign.Profile!.Deck);
            return _campaign.LoanerDecks().Where(l => _campaign.Rating(l) > yours).OrderByDescending(_campaign.Rating).FirstOrDefault();
        }

        /// <summary>
        /// Where a tag partner is picked, for the advice: "on the Tag Cup screen" at a Tag Cup; for a guest tag duel, a Tag
        /// Cup's screen by name plus a note that the guest duel brings the partner picked there. With no Tag Cup open yet,
        /// null and a note saying when the first one opens.
        /// </summary>
        private (string? where, string note) PartnerPickedWhere(bool guest)
        {
            var circuits = _campaign.Circuits;
            var profile = _campaign.Profile!;
            int open = Enumerable.Range(0, circuits.TagCups.Count).Where(c => circuits.IsTagCupOpen(c, profile)).DefaultIfEmpty(-1).First();
            if (open < 0)
                return (null, circuits.TagCups.Count > 0
                    ? $"Partners are picked on a Tag Cup's screen, and the first, {circuits.TagCups[0].Name}, opens after {circuits.All[circuits.TagCups[0].After].Name}."
                    : "Your partner is the first duelist you've beaten.");
            return guest
                ? ($"on a Tag Cup's screen (Campaign, then {circuits.TagCups[open].Name})", " A guest tag duel brings the partner you picked last there.")
                : ("on the Tag Cup screen", "");
        }
    }
}
