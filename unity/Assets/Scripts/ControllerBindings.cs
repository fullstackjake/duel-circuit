using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace YgoCampaign.Game
{
    public enum PadAction { Select, RightClick, Back, Extra, PagePrev, PageNext, Pause, Ask, CheckField, Terms }

    /// <summary>
    /// Which controller button does what, a saved setting. Buttons are named by their place on the pad (the Input
    /// System's names: buttonSouth is A on Xbox, Cross on PlayStation, B on Switch), shown with Xbox letters.
    /// Binding a button that another action has swaps the two, so every action keeps a button.
    /// </summary>
    public static class ControllerBindings
    {
        /// <summary>The buttons you can bind (the stick moves the cursor and the d-pad jumps it, so they stay put).</summary>
        public static readonly string[] Buttons =
        {
            "buttonSouth", "buttonEast", "buttonWest", "buttonNorth", "leftShoulder", "rightShoulder",
            "leftTrigger", "rightTrigger", "start", "select", "leftStickPress", "rightStickPress",
        };

        public static readonly IReadOnlyDictionary<PadAction, string> Defaults = new Dictionary<PadAction, string>
        {
            [PadAction.Select] = "buttonSouth", [PadAction.RightClick] = "buttonWest", [PadAction.Back] = "buttonEast",
            [PadAction.Extra] = "buttonNorth", [PadAction.PagePrev] = "leftShoulder", [PadAction.PageNext] = "rightShoulder",
            [PadAction.Pause] = "start", [PadAction.Ask] = "leftTrigger", [PadAction.CheckField] = "rightTrigger", [PadAction.Terms] = "select",
        };

        /// <summary>Checks set this false so remapping in a test never touches your saved buttons.</summary>
        public static bool Persist = true;

        private static Dictionary<PadAction, string>? _map;

        private static Dictionary<PadAction, string> Map
        {
            get
            {
                if (_map != null) return _map;
                _map = new Dictionary<PadAction, string>();
                foreach (var (action, button) in Defaults)
                {
                    var saved = Persist ? PlayerPrefs.GetString("pad." + action, "") : "";
                    _map[action] = Buttons.Contains(saved) ? saved : button;
                }
                // A damaged save could give two actions one button: those fall back to the defaults.
                if (_map.Values.Distinct().Count() != _map.Count) _map = Defaults.ToDictionary(p => p.Key, p => p.Value);
                return _map;
            }
        }

        public static string Get(PadAction action) => Map[action];

        public static void Bind(PadAction action, string button)
        {
            if (!Buttons.Contains(button)) return;
            var other = Map.FirstOrDefault(p => p.Value == button && p.Key != action);
            if (other.Value != null) Map[other.Key] = Map[action];
            Map[action] = button;
            Save();
        }

        public static void Reset()
        {
            _map = Defaults.ToDictionary(p => p.Key, p => p.Value);
            Save();
        }

        private static void Save()
        {
            if (!Persist) return;
            foreach (var (action, button) in Map) PlayerPrefs.SetString("pad." + action, button);
            PlayerPrefs.Save();
        }

        /// <summary>For checks: forget the loaded buttons (reloads them, from the save if Persist).</summary>
        public static void Reload() => _map = null;

        public static string Label(string button) => button switch
        {
            "buttonSouth" => "A", "buttonEast" => "B", "buttonWest" => "X", "buttonNorth" => "Y",
            "leftShoulder" => "LB", "rightShoulder" => "RB", "leftTrigger" => "LT", "rightTrigger" => "RT",
            "start" => "Start", "select" => "View", "leftStickPress" => "L3", "rightStickPress" => "R3",
            _ => button,
        };

        public static string Label(PadAction action) => Label(Get(action));

        public static string Name(PadAction action) => action switch
        {
            PadAction.Select => "Select / click",
            PadAction.RightClick => "Right-click",
            PadAction.Back => "Back / cancel",
            PadAction.Extra => "Extra action",
            PadAction.PagePrev => "Previous page",
            PadAction.PageNext => "Next page",
            PadAction.Pause => "Pause",
            PadAction.CheckField => "Check field",
            PadAction.Terms => "Card terms",
            _ => "Ask to chain",
        };

        public static string Help(PadAction action) => action switch
        {
            PadAction.Select => "Clicks what the cursor is on; hold it and move to drag a card.",
            PadAction.RightClick => "Right-clicks: a card's side panel and other second actions.",
            PadAction.Back => "Declines, cancels or closes, and puts the cursor back on the card you came from.",
            PadAction.Extra => "The screen's extra action (the deck editor: favorite a card).",
            PadAction.PagePrev => "The previous page of choices or cards.",
            PadAction.PageNext => "The next page of choices or cards.",
            PadAction.Pause => "Pauses and resumes a duel you're watching.",
            PadAction.CheckField => "Hold it to hide whatever covers the board (a question, a picker, a menu) and look at the field.",
            PadAction.Terms => "Explains the next blue term in the card detail's text (each press, the next one).",
            _ => "Hold it to be asked \"Chain?\" at the next chance, whatever the Chain setting.",
        };

        /// <summary>The hint line along the bottom of the screen while the controller is in use.</summary>
        public static string Legend() =>
            $"{Label(PadAction.Select)}  select  ·  hold {Label(PadAction.Select)}  drag  ·  {Label(PadAction.RightClick)}  right-click / Side  ·  "
            + $"{Label(PadAction.Back)}  back  ·  D-pad  jump  ·  {Label(PadAction.PagePrev)} / {Label(PadAction.PageNext)}  page  ·  hold {Label(PadAction.CheckField)}  check field  ·  {Label(PadAction.Terms)}  card terms  ·  {Label(PadAction.Pause)}  pause";

        // ---- Remapping: the next button pressed is bound ---------------------------------------------------

        /// <summary>The action waiting for a button press (Settings), or null.</summary>
        public static PadAction? Capturing { get; private set; }
        public static float CaptureUntil { get; private set; }
        private static Action? _captured;

        /// <summary>Waits (up to 8 seconds) for the next controller button and binds it to the action.</summary>
        public static void BeginCapture(PadAction action, Action done)
        {
            Capturing = action;
            CaptureUntil = Time.unscaledTime + 8f;
            _captured = done;
        }

        /// <summary>Ends the wait: binds the button if one was pressed (null: it timed out or was cancelled).</summary>
        public static void EndCapture(string? button)
        {
            if (Capturing is PadAction action && button != null) Bind(action, button);
            Capturing = null;
            var done = _captured;
            _captured = null;
            done?.Invoke();
        }
    }
}
