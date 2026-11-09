using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI;

namespace Ygo.Engine.Campaign
{
    /// <summary>
    /// Tag Cup partners. Anyone you've beaten can partner you with the deck you first met them with; the bond grows
    /// with every win against them: at <see cref="DeckBond"/> wins they'll bring any deck you've beaten them with, at
    /// <see cref="SharpBond"/> they play their sharpest. Kit, the shop regular, partners you from the start with one of
    /// your other campaign decks or a recipe you can borrow (a loaner), or Kit's own while there's neither.
    /// </summary>
    public static class TagPartners
    {
        public const string ShopRegularId = "kit";
        public const int DeckBond = 3, SharpBond = 5;
        /// <summary>How well a partner plays at <see cref="SharpBond"/>: a skilled player's level, planning ahead.</summary>
        public const double SharpSkill = 0.95;

        public static bool IsShopRegular(Personality p) => p.Id == ShopRegularId;

        /// <summary>
        /// The decks a partner can bring: the shop regular, any of <paramref name="yourDecks"/> but the one you play
        /// (their own while there's no other); anyone else their own first deck, and from <see cref="DeckBond"/> wins
        /// every deck you've beaten them with too.
        /// </summary>
        public static List<string> DeckChoices(Personality p, Circuits circuits, Profile profile, IEnumerable<string> yourDecks)
        {
            if (IsShopRegular(p))
            {
                var lent = yourDecks.Where(d => d != profile.Deck).ToList();
                return lent.Count > 0 ? lent : p.Decks.Take(1).ToList();
            }
            var decks = new List<string>();
            if (p.Decks.Count > 0) decks.Add(p.Decks[0]);
            if (profile.WinsAgainst(p.Id) < DeckBond) return decks;
            for (int c = 0; c < circuits.All.Count; c++)
                for (int m = 0; m < circuits.All[c].Matches.Count; m++)
                    if (circuits.All[c].Matches[m].Character.Id == p.Id && profile.HasWon(c, m)) decks.Add(circuits.All[c].Matches[m].Deck);
            for (int t = 0; t < circuits.TagCups.Count; t++)
                for (int m = 0; m < circuits.TagCups[t].Matches.Count; m++)
                {
                    if (!profile.MatchWins.ContainsKey(Circuits.TagKey(t, m))) continue;
                    var (lead, second) = circuits.TagCups[t].Matches[m];
                    if (lead.Character.Id == p.Id) decks.Add(lead.Deck);
                    if (second.Character.Id == p.Id) decks.Add(second.Deck);
                }
            return decks.Where(d => d.Length > 0).Distinct().ToList();
        }

        /// <summary>The deck a partner brings: the one you picked for them if it's still a choice, else the first.</summary>
        public static string DeckFor(Personality p, Circuits circuits, Profile profile, IEnumerable<string> yourDecks)
        {
            var choices = DeckChoices(p, circuits, profile, yourDecks);
            return profile.PartnerDecks.TryGetValue(p.Id, out var picked) && choices.Contains(picked) ? picked : choices.FirstOrDefault() ?? "";
        }

        /// <summary>How the partner plays: at <see cref="SharpBond"/> wins (or the shop regular) at least <see cref="SharpSkill"/>.</summary>
        public static Personality Playing(Personality p, Profile profile)
        {
            if ((!IsShopRegular(p) && profile.WinsAgainst(p.Id) < SharpBond) || p.Skill >= SharpSkill) return p;
            var sharp = p.Copy();
            sharp.Skill = SharpSkill;
            return sharp;
        }

        /// <summary>The bond in a few words: what it gives now, and what the next win brings.</summary>
        public static string Bond(Personality p, Profile profile)
        {
            if (IsShopRegular(p)) return "plays at full strength: one of your other decks, or any recipe you've unlocked";
            int wins = profile.WinsAgainst(p.Id);
            // The card unlocks at each bond (CardUnlocks): bond 3, one trade; bond 5, their signature.
            var gift = p.Signature.Length > 0 ? $"bond {SharpBond}: their {p.Signature}" : "";
            var cards = wins >= SharpBond ? "" : wins >= DeckBond ? (gift.Length > 0 ? " · " + gift : "")
                : $" · bond {DeckBond}: one trade" + (gift.Length > 0 ? " · " + gift : "");
            if (wins >= SharpBond) return $"beaten {wins}x · bond: any deck you've beaten them with, their sharpest play";
            if (wins >= DeckBond) return $"beaten {wins}x · bond: any deck you've beaten them with · {SharpBond - wins} more win{(SharpBond - wins == 1 ? "" : "s")}: sharper play{cards}";
            return $"beaten {wins}x · {DeckBond - wins} more win{(DeckBond - wins == 1 ? "" : "s")}: pick their deck{cards}";
        }
    }
}
