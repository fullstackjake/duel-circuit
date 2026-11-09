using System.IO;

namespace Ygo.Engine.Replays
{
    /// <summary>
    /// Watch the combo (docs/dev/HEADLINERS-2026-11-01.md): each ready headliner's best goldfish duel, its route played in full
    /// against a duelist that only sets a monster, saved by `--combo-goldfish` as a replay code in
    /// campaign/replays/&lt;headliner id&gt;.txt. The game's build copies campaign/**/*.txt, so the app finds it beside
    /// story.txt (its campaign folder) with <see cref="Load"/>. Seat 0 is the headliner. Play it back as any replay
    /// (attach <see cref="YataLock"/>, as replays do).
    /// </summary>
    public static class HeadlinerReplays
    {
        /// <summary>The folder under the campaign data folder.</summary>
        public const string Folder = "replays";

        /// <summary>Where a headliner's replay lives, given the campaign data folder (the one holding story.txt).</summary>
        public static string PathFor(string campaignFolder, string headliner) => Path.Combine(campaignFolder, Folder, headliner + ".txt");

        /// <summary>A headliner's Watch-the-combo replay, or null when it has none.</summary>
        public static Replay? Load(string campaignFolder, string headliner)
        {
            var path = PathFor(campaignFolder, headliner);
            return File.Exists(path) ? Replay.Load(path) : null;
        }

        /// <summary>Whether a headliner has one (the Unlisted page's Watch the combo button).</summary>
        public static bool Has(string campaignFolder, string headliner) => File.Exists(PathFor(campaignFolder, headliner));
    }
}
