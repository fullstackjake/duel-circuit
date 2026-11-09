using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The board's side of developer mode (DuelController.DevMode): the gear offers the developer panel only then, and
    /// the panel's tools that change a duel or the save (the matchup arrows, Start Duel, Random, the Deck Editor,
    /// Rematch, Replays) are shut, with a note why, while a campaign duel or a story scene is under way.
    /// </summary>
    public sealed partial class BoardView
    {
        /// <summary>Developer mode: the gear offers the developer panel ("Free play tools").</summary>
        public bool DeveloperMode { get; set; }

        /// <summary>Why the duel-changing tools are off right now (a campaign duel, a story scene), or null when they're on.</summary>
        public Func<string?>? DuelToolsLock;

        private readonly List<Button> _duelTools = new List<Button>();
        private Text? _duelToolsNote;
        private bool _duelToolsApplied;
        private string? _duelToolsReason;

        /// <summary>For checks: whether the panel's duel-changing tools are shut, the note, and how many are clickable.</summary>
        public (bool locked, string note, int on) DuelToolsState =>
            (_duelToolsReason != null, _duelToolsNote?.text ?? "", _duelTools.Count(b => b.interactable));

        /// <summary>For checks: Start Duel, Random, the Deck Editor, Rematch and Replays asked for as clicks would (even shut ones).</summary>
        public void DebugDuelTools()
        {
            StartClicked?.Invoke();
            RandomClicked?.Invoke();
            DecksClicked?.Invoke();
            RematchClicked?.Invoke();
            ReplaysClicked?.Invoke();
        }

        /// <summary>Each frame with the panel up: the tools follow the lock (a duel can start or end under it).</summary>
        private void UpdateDuelTools()
        {
            if (!_devPanel) return;
            var reason = DuelToolsLock?.Invoke();
            if (_duelToolsApplied && reason == _duelToolsReason) return;
            _duelToolsApplied = true;
            _duelToolsReason = reason;
            foreach (var b in _duelTools) b.interactable = reason == null;
            if (_duelToolsNote != null)
                _duelToolsNote.text = reason == null ? "" : $"{reason}: these tools are off until it's over.";
        }
    }
}
