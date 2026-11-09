using System;
using System.Collections.Generic;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Last input wins (docs/dev/HOVER-DESIGN-2026-11-07.md, section 3): the mouse (or the stick's cursor) moving more than 2 px
    /// is pointer mode, where hover shows and the keyboard's gold frame (<see cref="KeyFrame"/>) hides; an arrow key, or the
    /// d-pad driving a screen's highlight (<see cref="ControllerSupport.HighlightMode"/>), is highlight mode, where the frame
    /// shows and nothing lights up under a resting mouse. Read by <see cref="UiHover"/> and <see cref="KeyFrame"/>; polled
    /// once a frame by a small driver made at startup (no scene object needed).
    /// </summary>
    public static class InputMode
    {
        /// <summary>True while the pointer was the last thing used (the start: a mouse game).</summary>
        public static bool Pointer { get; private set; } = true;

        /// <summary>Raised when <see cref="Pointer"/> flips.</summary>
        public static event Action? Changed;

        /// <summary>An arrow key was the last thing pressed (until the mouse moves).</summary>
        private static bool _keys;
        /// <summary>Each mouse's position when last looked at (the stick's virtual one and a check's test mouse included).</summary>
        private static readonly Dictionary<int, Vector2> Anchors = new Dictionary<int, Vector2>();
        private const float MoveSqr = 2f * 2f;

        /// <summary>An arrow key (or the d-pad on a highlight) drove the screen: highlight mode now.</summary>
        public static void KeysUsed()
        {
            _keys = true;
            Refresh();
        }

        /// <summary>Once a frame: the mice moved, the arrows pressed, the pad's highlight mode.</summary>
        internal static void Poll()
        {
#if ENABLE_INPUT_SYSTEM
            var devices = UnityEngine.InputSystem.InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                if (device is UnityEngine.InputSystem.Mouse mouse)
                {
                    var at = mouse.position.ReadValue();
                    if (!Anchors.TryGetValue(mouse.deviceId, out var anchor)) { Anchors[mouse.deviceId] = at; continue; }
                    // From where it was when the keys took over: a slow drift adds up, a 1 px jitter doesn't.
                    if ((at - anchor).sqrMagnitude > MoveSqr)
                    {
                        _keys = false;
                        Anchors[mouse.deviceId] = at;
                    }
                    else if (!_keys) Anchors[mouse.deviceId] = at;
                }
                // Arrows moving the caret in a text field (the shop's search) aren't the keys driving the screen.
                else if (device is UnityEngine.InputSystem.Keyboard k && Arrow(k) && !ArrowNav.TypingSomewhere())
                    _keys = true;
            }
#endif
            Refresh();
        }

#if ENABLE_INPUT_SYSTEM
        private static bool Arrow(UnityEngine.InputSystem.Keyboard k) =>
            k.leftArrowKey.wasPressedThisFrame || k.rightArrowKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame;
#endif

        /// <summary>
        /// An arrow key or the d-pad went down this frame (outside a text field): the UI's navigation moving its selection,
        /// even before <see cref="Poll"/> has switched to highlight mode this frame.
        /// </summary>
        public static bool NavigatedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (ArrowNav.TypingSomewhere()) return false;
            var devices = UnityEngine.InputSystem.InputSystem.devices;
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i] is UnityEngine.InputSystem.Keyboard k && Arrow(k)) return true;
                if (devices[i] is UnityEngine.InputSystem.Gamepad pad
                    && (pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame)) return true;
            }
#endif
            return false;
        }

        private static void Refresh()
        {
            bool pad = false;
#if ENABLE_INPUT_SYSTEM
            pad = ControllerSupport.HighlightMode;
#endif
            bool pointer = !(_keys || pad);
            if (pointer == Pointer) return;
            Pointer = pointer;
            Changed?.Invoke();
        }

        /// <summary>For checks: back to pointer mode, as at the start (the next mouse move would do the same).</summary>
        public static void DebugReset()
        {
            _keys = false;
            Anchors.Clear();
            Refresh();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("InputMode") { hideFlags = HideFlags.HideInHierarchy };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<InputModeDriver>();
        }
    }

    /// <summary>Polls <see cref="InputMode"/> and runs the hover fades, after the screens' own Updates.</summary>
    [DefaultExecutionOrder(100)]
    internal sealed class InputModeDriver : MonoBehaviour
    {
        private void Update()
        {
            InputMode.Poll();
            UiHover.TickAll(Time.unscaledDeltaTime);
        }
    }
}
