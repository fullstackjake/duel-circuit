using System;
using System.Collections.Generic;
using UnityEngine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A campaign duel's results screen, which waits 2 s on the finished board first. Two rules keep that wait safe:
    /// the screen belongs to its duel (another duel started, or Rematch asked for, in those 2 s drops it), and its scenes
    /// never depend on it showing: they're queued when the duel ends (the alley's queue, <see cref="CampaignState.QueueAlleyStory"/>),
    /// so the next campaign screen plays them when the results never show (the menu, the editor or the shop opened), and
    /// the next results screen to show takes them all. The queue is in the save too, until its scenes play: quitting the app
    /// in those 2 s, or with the results screen up, loses nothing (the next campaign screen after a load plays them).
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>
        /// Counts what takes the board's place: a duel started (a replay watched counts), a Rematch asked for, the dev
        /// panel's Start or Random, a menu choice, the campaign screen opening. A results screen waiting its 2 s shows only
        /// while this is still what it was when its duel ended.
        /// </summary>
        private int _duelSerial;

        private static readonly IReadOnlyDictionary<string, string> NoFill = new Dictionary<string, string>();

        /// <summary>
        /// A finished duel's results, after the board for a moment: <paramref name="events"/> (its scenes, grant events
        /// included) are queued now; after 2 s <paramref name="show"/> gets the queue (these and any a skipped screen left),
        /// unless another duel started or a screen opened over the board, in which case they stay queued.
        /// </summary>
        private void ShowResultsSoon(List<string> events, IReadOnlyList<Grant> grants, Action<List<CampaignState.QueuedStory>> show,
            string partner = "", IReadOnlyDictionary<string, string>? fill = null)
        {
            _campaign.QueueAlleyStory(events, fill ?? NoFill, grants, partner);
            StartCoroutine(After(_duelSerial));
            System.Collections.IEnumerator After(int serial)
            {
                yield return new WaitForSecondsRealtime(2f);
                if (!ResultsDue(serial)) yield break;
                show(_campaign.TakeQueuedStory());
            }
        }

        /// <summary>Whether the results of the duel that ended at <paramref name="serial"/> can show now.</summary>
        private bool ResultsDue(int serial) =>
            serial == _duelSerial && !_editor.IsOpen && !_shop.IsOpen && !_menu.IsOpen && !_browser.IsOpen && !_settings.IsOpen && !_about.IsOpen
            && !_storyScreen.IsOpen;

        /// <summary>
        /// A results screen's way out other than Continue (Rematch, the Card Shop): its scenes go back in the queue, for the
        /// next campaign screen or results screen, then <paramref name="then"/>.
        /// </summary>
        private Action Requeued(List<CampaignState.QueuedStory> story, Action then) => () =>
        {
            _campaign.Requeue(story);
            then();
        };

        /// <summary>
        /// Queued batches played in order, each with its own placeholders, grants and partner (a results screen's Continue,
        /// the campaign screen's queue), then <paramref name="then"/>. Each batch leaves the save's queue once its scenes played
        /// (<see cref="CampaignState.StoryPlayed"/>): a quit in the middle replays only what's left, and nothing twice.
        /// </summary>
        private void PlayResultStory(IReadOnlyList<CampaignState.QueuedStory> story, Action then, int from = 0)
        {
            if (from >= story.Count) { then(); return; }
            var batch = story[from];
            // Off the queue as its scenes end, in the story's own closing save (one write); a batch with nothing left to play saves here.
            bool saved = false;
            PlayStory(batch.Events, batch.Partner, () =>
            {
                if (!saved) _campaign.StoryPlayed(batch);
                PlayResultStory(story, then, from + 1);
            }, batch.Grants, batch.Fill, played: () => { _campaign.StoryPlayed(batch, save: false); saved = true; });
        }
    }
}
