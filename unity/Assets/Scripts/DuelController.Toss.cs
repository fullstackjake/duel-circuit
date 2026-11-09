using System;
using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.AI.Playbooks;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The coin toss before every duel you start from a versus screen or Free Duel: whoever wins it chooses who takes the
    /// first turn. You choose with Go first / Go second; a character goes first unless its deck's playbook would rather
    /// go second (Mekk-Knights). The screen's tip: how this very team did going first and second where that was measured
    /// (the wall study); else, outside tag duels, your deck's preference (a deck's preference is for one-on-one duels: in
    /// the measured tag matches going first and second came out even overall). The duel gets the result as <see cref="Ygo.Engine.DuelSettings.FirstPlayer"/> (a core
    /// patch, patches/ygopro-core). Automated checks skip the toss and keep you first, except the check of the toss.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Who takes the next duel's first turn (0 you / the left side, 1 the other), set by the toss; NewDuel uses it once.</summary>
        private int _nextFirstPlayer;
        /// <summary>The last toss's result (0 you / the left side first, 1 the other), kept for QA after the duel starts.</summary>
        private int _tossFirstPlayer;
        /// <summary>Checks: no toss; you go first (or second, with -gosecond).</summary>
        private bool _autoToss;
        /// <summary>Who takes turn 1 when there's no toss: 0 you, 1 the other side (-gosecond: checks run going second).</summary>
        private int _checkFirstPlayer;
        /// <summary>What the last toss screen suggested if you win it (go first?); QA follows it like a player reading the tip.</summary>
        private bool _tossSuggestsFirst = true;

        /// <summary>Who took this duel's first turn (0 you / the left side, 1 the other; -1 before it starts).</summary>
        public int FirstTurnPlayer { get; private set; } = -1;

        /// <summary>Whether the left side (you) wins the toss for this duel's seed (the same every time, for replays and checks).</summary>
        private static bool LeftWinsToss(int duelSeed) => (Scramble(duelSeed) & 1) == 0;

        /// <summary>
        /// A duel seed scrambled (SplitMix64's finalizer), for a draw per duel. The duel seed goes up by one each duel, and
        /// System.Random's first value barely changes between neighboring seeds: the toss came in streaks of five or six
        /// wins, then as many losses (QA 2026-11-01). Scrambled, neighbors are independent.
        /// </summary>
        private static int Scramble(int duelSeed)
        {
            ulong z = (ulong)(uint)duelSeed + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)((z ^ (z >> 31)) & 0x7FFFFFFF);
        }

        /// <summary>The deck a playbook knows it by (a loaner by its recipe).</summary>
        private static string PlaybookDeck(string deck) => Loaners.IsLoaner(deck) ? Loaners.RecipeOf(deck) : deck;

        /// <summary>
        /// The toss, then <paramref name="start"/> with <see cref="_nextFirstPlayer"/> set. <paramref name="youChoose"/>:
        /// the left side is you (else a character, who decides like any other). <paramref name="game"/> 2 or 3 of a best-of-three
        /// final: no toss, the last game's loser chooses (<paramref name="leftChooses"/>: whether that's the left side).
        /// </summary>
        private void CoinToss(string left, string right, string leftDeck, string rightDeck, Action start, bool youChoose = true,
            bool tag = false, (string hint, bool first)? measured = null, bool? leftChooses = null, int game = 0)
        {
            if (_autoToss)
            {
                _nextFirstPlayer = _checkFirstPlayer;
                start();
                return;
            }
            bool leftWins = leftChooses ?? LeftWinsToss(seed);
            // A later game of a best of three: the screen says who lost the last one, and that they choose.
            bool series = game > 1 && leftChooses != null;
            string title = series ? $"GAME {game}" : "COIN TOSS";
            string subtitle = series ? "Best of three: the last game's loser chooses who takes the first turn." : "The winner chooses who takes the first turn.";
            string won = series ? $"lost game {game - 1}" : "won the toss";
            bool rightFirst = Playbooks.GoesFirst(PlaybookDeck(rightDeck));
            if (leftWins && !youChoose)
            {
                // A character on the left (watching): it decides like the other side would.
                bool leftFirst = Playbooks.GoesFirst(PlaybookDeck(leftDeck));
                _scenes.ShowCoinToss(right, left, false, leftFirst, "", rightGoesFirst => { _tossFirstPlayer = _nextFirstPlayer = rightGoesFirst ? 1 : 0; start(); },
                    title, subtitle, won);
                return;
            }
            var why = tag ? null : Playbooks.WhySecond(PlaybookDeck(leftDeck));
            var hint = measured?.hint ?? (why != null ? $"Your deck works best going second: {why}." : "");
            _tossSuggestsFirst = measured?.first ?? why == null;
            _scenes.ShowCoinToss(left, right, leftWins, rightFirst, hint, leftGoesFirst =>
            {
                _tossFirstPlayer = _nextFirstPlayer = leftGoesFirst ? 0 : 1;
                start();
            }, title, subtitle, won);
        }

        /// <summary>
        /// The toss tip at a measured match (campaign/wall-advice.txt, and for circuit matches campaign/toss-advice.txt) for
        /// the team you're bringing: your deck, and in a tag duel your partner and their deck. Null when that team wasn't
        /// measured there, or first and second were within 20. Only your side reads it: the CPUs choose as their decks do.
        /// </summary>
        private (string hint, bool first)? MeasuredToss(string matchKey, string partnerId = "", string partnerDeck = "") =>
            MeasuredToss(_campaign.Advice.For(matchKey).Concat(_campaign.TossAdvice.For(matchKey)), partnerId, partnerDeck);

        /// <summary>The same from a match's measured options (a Library cup match's: campaign/library-advice.txt).</summary>
        private (string hint, bool first)? MeasuredToss(IEnumerable<MeasuredOption> measured, string partnerId = "", string partnerDeck = "")
        {
            var yourDeck = _versusDeck ?? _campaign.Profile!.Deck;
            foreach (var o in measured)
            {
                bool deck = o.Borrows ? Loaners.NameFor(o.Deck) == yourDeck : !Loaners.IsLoaner(yourDeck);
                if (!deck || o.Partner != partnerId) continue;
                if (partnerId.Length > 0 && o.PartnerDeck != partnerDeck && Loaners.NameFor(o.PartnerDeck) != partnerDeck) continue;
                if (o.YouFirst < 0 || o.YouSecond < 0 || Math.Abs(o.YouFirst - o.YouSecond) < 20) return null;
                bool first = o.YouFirst > o.YouSecond;
                int best = Math.Max(o.YouFirst, o.YouSecond), other = Math.Min(o.YouFirst, o.YouSecond);
                return ($"Measured with this {(partnerId.Length > 0 ? "team" : "deck")} here: going {(first ? "first" : "second")} won {best}%, going {(first ? "second" : "first")} {other}%.", first);
            }
            return null;
        }

        /// <summary>For checks: the seed's toss winner, so a check can pick a toss it wins or loses.</summary>
        public static bool DebugLeftWinsToss(int duelSeed) => LeftWinsToss(duelSeed);
    }
}
