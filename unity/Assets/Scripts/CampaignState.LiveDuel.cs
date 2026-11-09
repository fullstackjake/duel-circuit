using System.Collections.Generic;
using System.Linq;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The campaign duel of yours being played, in the save (<see cref="Profile.LiveDuel"/>): begun at Duel! (before the coin
    /// toss, as the alley holds its stakes and a Locals match goes live), cleared with its result, and settled as that kind's
    /// loss when a save loads with one (the app closed or crashed mid-duel), with a notice, as the alley's duels are. The
    /// in-duel menu's Quit leaves it to that load, as Cmd-Q and a crash do: one path for every way out.
    /// </summary>
    public sealed partial class CampaignState
    {
        /// <summary>
        /// Duel! at a circuit, Tag Cup, guest, Library cup, final round, Locals or ladder versus screen, and the duel's start: it's
        /// live in the save from here. The same duel again (its start after its Duel!) keeps its record; another duel still
        /// live (one left without a result) is settled as a loss first. Saves.
        /// </summary>
        public void BeginLiveDuel(LiveDuel live)
        {
            if (Profile == null) return;
            if (Profile.LiveDuel is LiveDuel was && was.Kind == live.Kind && was.Event == live.Event && was.Match == live.Match
                && was.Guest == live.Guest && was.Tag == live.Tag)
            {
                if (was.Loaner == live.Loaner) return;
            }
            else SettleLiveDuel();
            Profile.LiveDuel = live;
            Save();
        }

        /// <summary>The live duel's result is recorded: its record goes with the same save.</summary>
        public void EndLiveDuel()
        {
            if (Profile != null) Profile.LiveDuel = null;
        }

        /// <summary>
        /// A duel left live, settled as its kind's loss (<see cref="LiveDuels.Settle"/>, the card unlocks included): the notice
        /// for the campaign screen ("You left your match against ... mid-duel. That counts as a loss.") and the scenes its
        /// results screen would have played. True when the save changed (a record that names nothing is dropped too).
        /// </summary>
        private bool SettleLiveDuel()
        {
            if (Profile?.LiveDuel == null) return false;
            var settled = LiveDuels.Settle(Profile, Circuits, Library, Guests, Unlocks, Matchups, id => _roster.FirstOrDefault(p => p.Id == id)?.Name ?? id,
                Ladder);
            if (settled == null) return true;
            Announce(settled.Reward.Grants);
            AlleyNotice = string.Join("  ·  ", new[] { AlleyNotice, settled.Summary }.Where(s => s.Length > 0));
            QueueAlleyStory(settled.Events, new Dictionary<string, string>(), settled.Reward.Grants);
            return true;
        }

        /// <summary>What a load does for a duel left live: settles it and saves.</summary>
        private void AfterLiveLoad()
        {
            if (SettleLiveDuel()) Save();
        }
    }
}
