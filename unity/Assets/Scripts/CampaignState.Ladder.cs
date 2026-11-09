using System.Collections.Generic;
using Ygo.Engine.Campaign;
using Ygo.Engine.DeckBuilding;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Champion's Ladder (docs/dev/CHAMPIONS-LADDER-SPEC-2026-11-06.md): campaign/ladder.txt loaded after the Library (its
    /// opening asks the Every Era final), your standing on the save (<see cref="Profile.Ladder"/>), its rules (the last
    /// circuit's, in every cycle, as Ilse's table) and its results. The board is CampaignScreen.Ladder, the duels
    /// DuelController.Ladder.
    /// </summary>
    public sealed partial class CampaignState
    {
        /// <summary>The ladder as campaign/ladder.txt has it (empty without the file). Set in the constructor, after the Library.</summary>
        public Ladder Ladder { get; private set; } = Ladder.Empty;

        /// <summary>Whether the ladder's opening scene is due on this Open ("ladder": open, and ladder-opens not seen yet).</summary>
        public bool LadderDue => Profile != null && Ladder.OpenDue(Profile, Library);

        /// <summary>The circuit whose rules ladder duels follow: the last one (the Tier 0 Gauntlet), never the Beginner list in New Game+.</summary>
        public int LadderRulesCircuit => Ladder.RulesCircuit(Circuits);

        /// <summary>
        /// The ladder's rules for your deck: the Tier 0 Gauntlet's list (<see cref="CircuitList"/>, TCG September 2026) in open
        /// and era-limited play alike (owner, 2026-11-06: as the spec says; Ilse's table keeps following the mode), your Ban
        /// Passes, the format's pool in era-limited play, and the circuit's texts.
        /// </summary>
        public FormatRules LadderRules
        {
            get
            {
                var rules = Rules(LadderRulesCircuit);
                rules.List = CircuitList(LadderRulesCircuit);
                return rules;
            }
        }

        /// <summary>The list the ladder plays under, by name ("" when it isn't installed).</summary>
        public string LadderListName => CircuitList(LadderRulesCircuit)?.Name ?? "";

        /// <summary>
        /// Why a deck can't climb, or nothing when it can: a deck you built (no loaner, no partner's edit: decision 7), legal
        /// under <see cref="LadderRules"/> with cards you own. The board and the versus both ask this.
        /// </summary>
        public List<DeckProblem> LadderProblems(string deck)
        {
            if (IsLoanerDeck(deck))
                return new List<DeckProblem> { new DeckProblem { Message = "It's a loaner: the ladder takes only decks you built." } };
            if (!_library.Contains(deck)) return new List<DeckProblem> { new DeckProblem { Message = "Build a deck first (New deck)." } };
            if (!IsYours(deck))
                return new List<DeckProblem> { new DeckProblem { Message = "It's your edit of a partner's deck, theirs to bring: the ladder takes only decks you built." } };
            return Problems(_library.Load(deck), LadderRules);
        }

        /// <summary>Records a ladder duel against <paramref name="seat"/> (null: nothing recorded, a seat you can't challenge now).</summary>
        public LadderResult? RecordLadder(LadderSeat seat, int winner) =>
            Profile == null ? null : Ladder.Record(Profile, Profile.Ladder, Library, seat, winner);

        /// <summary>Loads the ladder (the constructor's, right after the Library and the guests): a seat whose deck isn't installed is left out.</summary>
        private void LoadLadder(string campaignFolder, IEnumerable<Ygo.Engine.AI.Personality> roster)
        {
            Ladder = Ladder.Load(System.IO.Path.Combine(campaignFolder, Ladder.FileName), roster, _library.Contains);
            foreach (var problem in Ladder.Problems) UnityEngine.Debug.LogWarning($"ladder.txt: {problem}");
        }
    }
}
