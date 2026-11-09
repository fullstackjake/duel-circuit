using System;
using System.Linq;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The campaign check's step for developer mode (DuelController.DevMode), in its first campaign duel: a normal launch
    /// has no developer panel; in developer mode the panel opens but its duel-changing tools are shut and asking anyway
    /// does nothing; the duel's Menu offers only the way back, Concede, Settings, About and Quit, and Back to the duel returns.
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator DevModeCampaignCheck(Action<string, bool> report, Func<string, System.Collections.IEnumerator> shot)
        {
            bool devBefore = DeveloperMode, pausedBefore = _paused;
            var duelBefore = _duel;
            var decision = _view.Prompt.Current;
            bool DuelUntouched() => _duel == duelBefore && _runner != null && !_runner.IsFinished && CampaignDuelLive;

            // A normal launch: F12 shows nothing, and the gear has no "Free play tools".
            DebugDeveloperMode(false);
            ToggleDevPanel(); // what F12 does
            yield return null;
            _view.ToggleGear();
            yield return null;
            var gear = _view.GearRows();
            _view.ToggleGear();
            report($"a normal launch: F12 opens no developer panel and the gear doesn't offer one ({string.Join(", ", gear)})",
                _view.PlayLayout && gear.Count > 0 && !gear.Any(r => r.StartsWith("Free play tools")));

            // Developer mode in a campaign duel: the panel opens with its duel-changing tools shut and a note why.
            DebugDeveloperMode(true);
            ToggleDevPanel();
            yield return null;
            yield return null;
            yield return shot("dev-panel-campaign");
            var tools = _view.DuelToolsState;
            report($"developer mode in a campaign duel: the panel opens with its duel-changing tools shut ({tools.on} on, \"{tools.note}\")",
                !_view.PlayLayout && tools.locked && tools.on == 0 && tools.note.Contains("campaign duel"));
            _view.DebugDuelTools();
            yield return null;
            report("and Start Duel, Random, the Deck Editor, Rematch and Replays asked for anyway change nothing",
                DuelUntouched() && !_editor.IsOpen && !_browser.IsOpen && !_scenes.IsOpen);
            ToggleDevPanel();
            DebugDeveloperMode(devBefore);
            yield return null;

            // The duel's Menu: Back to the duel, Concede, Settings, About, Quit; Tag Duel, the Deck Editor and Replays shut.
            OpenMenuFromDuel();
            yield return new WaitForSecondsRealtime(0.3f);
            yield return shot("menu-mid-duel");
            var items = _menu.DebugItems;
            var shut = items.Where(i => i.locked).Select(i => i.choice).ToList();
            report($"the Menu over a campaign duel: {string.Join(", ", items.Select(i => i.locked ? $"({i.title})" : i.title))}",
                items.Count == 8 && items[0].choice == MainMenu.Choice.BackToDuel && items[1].choice == MainMenu.Choice.Concede
                && shut.Count == 3 && shut.Contains(MainMenu.Choice.TagDuel) && shut.Contains(MainMenu.Choice.DeckEditor) && shut.Contains(MainMenu.Choice.Replays)
                && items.Any(i => i.choice == MainMenu.Choice.Settings && !i.locked) && items.Any(i => i.choice == MainMenu.Choice.About && !i.locked)
                && items.Any(i => i.choice == MainMenu.Choice.Quit && !i.locked));
            _menu.DebugChoose(MainMenu.Choice.DeckEditor);
            _menu.DebugChoose(MainMenu.Choice.Replays);
            _menu.DebugChoose(MainMenu.Choice.TagDuel);
            yield return null;
            report("its shut options stay shut (the Deck Editor, Replays, Tag Duel)", _menu.IsOpen && !_editor.IsOpen && !_browser.IsOpen && !_tagSetup.IsOpen && DuelUntouched());
            _menu.DebugChoose(MainMenu.Choice.BackToDuel);
            yield return null;
            report("Back to the duel returns to it as it was", !_menu.IsOpen && DuelUntouched() && _paused == pausedBefore && _view.Prompt.Current == decision);
        }
    }
}
