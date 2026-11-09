using System;
using System.Linq;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Developer mode. The developer panel (F12, the gear's "Free play tools": the matchup picker, Start Duel, Random,
    /// Rematch, Replays, the Deck Editor, the decision list and log) exists only here. It's on with <c>-dev</c> (or
    /// <c>-devpanel</c>, which also opens the panel), in the Unity editor and development builds, and for the checks'
    /// flags (<c>-boardcheck</c> and the other <c>-*check</c>s, <c>-autoshot</c>, <c>-fxshots</c> ...). A normal launch has
    /// none of it: F12 does nothing and the gear doesn't offer it.
    /// <para>
    /// Even in developer mode, the tools that change a duel or the save are off while a campaign duel is under way (a
    /// circuit match, a Tag Cup, a Library cup or round, a guest, an alley table, a bracket game or the bracket's watched
    /// final; a lesson too) or a story scene is up, with a note why; the panel's view-only parts stay. Free Duel keeps them.
    /// </para>
    /// <para>
    /// The in-duel Menu over a campaign duel is the same kind of door: it offers Back to the duel, Concede (a loss),
    /// Settings and Quit, and keeps Free Duel, Tag Duel, the Deck Editor, Replays and the campaign screen closed until
    /// the duel is over (an alley duel left for the menu is settled as a loss first, DuelController.Alley). Its Quit records
    /// nothing: the save has the duel live, and the next launch settles it as a loss (DuelController.LiveDuel).
    /// </para>
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Launch flags that turn developer mode on besides the <c>-*check</c>s (they drive its tools).</summary>
        private static readonly string[] DevFlags =
            { "-dev", "-devpanel", "-autoshot", "-autoshot-end", "-editor", "-fxshots", "-foilshowcase", "-gradeshowcase", "-slabreveal", "-autoclick", "-qareplay" };

        /// <summary>The developer panel and its tools exist (see the class notes). Checks may switch it to test both sides.</summary>
        public static bool DeveloperMode { get; private set; }

        private static bool DeveloperLaunch(string[] args) =>
            Application.isEditor || Debug.isDebugBuild
            || args.Any(a => DevFlags.Contains(a) || (a.Length > 6 && a.StartsWith("-") && a.EndsWith("check")));

        /// <summary>A campaign duel: its result counts in the save. Free Duel, exhibitions and lessons aren't.</summary>
        private bool CampaignDuel =>
            _campaignMatch != null || _campaignTag != null || _campaignCup != null || _campaignRound != null || _guestDuel != null || _ladderDuel != null
            || _alleyTable != null || _bracketMatch != null || _bracketWatch;

        private bool DuelLive => _runner != null && !_runner.IsFinished && _watching == null && _showDuel == null;

        /// <summary>A campaign duel is being played (or its bracket final watched) and isn't over.</summary>
        private bool CampaignDuelLive => DuelLive && CampaignDuel;

        /// <summary>Why the tools that change a duel or the save are off now, or null when they're on.</summary>
        private string? DuelToolsLock() =>
            _storyScreen != null && _storyScreen.IsOpen ? "A story scene is playing"
            : CampaignDuelLive ? "A campaign duel is under way"
            : DuelLive && (_chainLesson != null || _lessonJudge != null) ? "A lesson is under way" // it has its own Leave lesson
            : null;

        /// <summary>A duel-changing tool asked for while it's locked: refused, with a line in the log saying why.</summary>
        private bool DuelToolRefused(string tool)
        {
            if (DuelToolsLock() is not string why) return false;
            _view.AppendLog($"{tool} is off: {why.ToLowerInvariant()}. Finish or concede the duel first.");
            return true;
        }

        /// <summary>F12 / the gear: the developer panel on or off (developer mode only).</summary>
        private void ToggleDevPanel()
        {
            if (!DeveloperMode)
            {
                if (_view.DevPanel) _view.DevPanel = false;
                return; // a normal launch has no developer panel
            }
            _view.DevPanel = !_view.DevPanel;
            _view.AppendLog(_view.DevPanel ? "Free play tools on (F12 to hide)." : "Free play tools off.");
            if (_view.DevPanel && DuelToolsLock() is string why)
                _view.AppendLog($"{why}: Start, Random, Rematch, Replays and the Deck Editor stay off until it's over.");
        }

        private bool _pausedBeforeMenu;
        private UiTheme? _themeBeforeMenu;

        /// <summary>The duel's Menu button: an alley duel left is settled as a loss; any other campaign duel waits behind the menu.</summary>
        private void OpenMenuFromDuel()
        {
            AbandonAlleyDuel();
            AbandonAlleyBracketDuel();
            _pausedBeforeMenu = _paused;
            _themeBeforeMenu = UiTheme.Current;
            MainStreetTheme();
            _paused = true;
            UpdateControls();
            _menu.Open();
        }

        /// <summary>Back to the duel from the menu over it, as it was (running, or paused if it was), in its own look.</summary>
        private void ResumeFromMenu()
        {
            _menu.Close();
            if (_themeBeforeMenu != null && UiTheme.Current != _themeBeforeMenu) UiTheme.Current = _themeBeforeMenu;
            _themeBeforeMenu = null;
            _paused = _pausedBeforeMenu;
            UpdateControls();
            if (_board != null) _view.Render(_board);
        }

        /// <summary>For checks: developer mode on or off (the checks get it from their flags; this tests a normal launch).</summary>
        private void DebugDeveloperMode(bool on)
        {
            DeveloperMode = on;
            _view.DeveloperMode = on;
            if (!on && _view.DevPanel) _view.DevPanel = false;
        }
    }
}
