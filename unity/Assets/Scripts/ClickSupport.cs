#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Clicks that land the first time, on every screen:
    /// <list type="bullet">
    /// <item>The first click on the window while another app is in front counts. The Input System keeps the mouse on when the
    /// app loses focus (the game runs in the background, and by default the mouse was switched off and reset, so the click
    /// that brings the window back could arrive at a switched-off mouse); and the window's views accept the first mouse
    /// (AppKit's default only brings the window forward).</item>
    /// <item>A press only turns into a drag (a list scrolling, a card picked up) after <see cref="DragThresholdPoints"/> screen
    /// points, the same on Retina and 1x (Unity's default is 10 pixels: 5 points on Retina, less than a trackpad press moves).</item>
    /// <item>A list whose rows all fit (<see cref="ScrollFit"/>) doesn't scroll, so it can't take a press as a drag.</item>
    /// </list>
    /// Focus changes, and the mouse's buttons for a few seconds after the window comes back, go to the log ("clicks: ..."):
    /// what the owner reads to confirm the first click arrives.
    /// </summary>
    public sealed class ClickSupport : MonoBehaviour
    {
        /// <summary>How far (screen points) a press moves before it's a drag.</summary>
        public const float DragThresholdPoints = 10f;

        /// <summary>For checks: lists that fit don't scroll (off: the old behaviour, for comparison).</summary>
        public static bool FitScrolls = true;
        /// <summary>For checks: a fixed drag threshold in pixels (null: <see cref="DragThresholdPoints"/> on this display).</summary>
        public static int? ThresholdOverride;

        /// <summary>The drag threshold in use (pixels), for checks and the log.</summary>
        public static int Threshold { get; private set; } = 10;

        /// <summary>The window's pixels per point, as the threshold last used it (0 until read).</summary>
        public static float PixelsPerPoint { get; private set; }

        /// <summary>Which view classes now accept the first mouse (null until installed), for checks and the log.</summary>
        public static string? FirstMouseReport { get; private set; }

        private float _nextThreshold, _nextScan, _focusGainedAt = -100f;
        private int _firstMouseTries;
        private bool _firstMouseDone;
        private int _buttonsLogged;
        private readonly List<GameObject> _roots = new List<GameObject>();
        private readonly List<ScrollRect> _scrolls = new List<ScrollRect>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            // The game runs in the background (DuelController): keep the mouse working across focus changes, so the
            // click that brings the window back isn't sent to a switched-off device.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var go = new GameObject("ClickSupport");
            DontDestroyOnLoad(go);
            go.AddComponent<ClickSupport>();
        }

        private void Start()
        {
            // A check run answers only its own test devices: the real mouse (the owner working beside it, the window in
            // the background) stays off, so a pointer that wanders over the window never hovers or clicks in a check.
            if (DuelController.IsCheckRun)
                foreach (var device in InputSystem.devices)
                    if (device is Mouse m && m.name != "ControllerCursor" && m.enabled)
                    {
                        InputSystem.DisableDevice(m);
                        Debug.Log($"clicks: check run, the real mouse ({m.name}) is off");
                    }
            Application.focusChanged += OnFocusChanged;
            InputSystem.onEvent += OnInputEvent;
            Debug.Log($"clicks: background behavior {InputSystem.settings.backgroundBehavior}, run in background {Application.runInBackground}, "
                      + $"focused {Application.isFocused}");
        }

        private void OnDestroy()
        {
            Application.focusChanged -= OnFocusChanged;
            InputSystem.onEvent -= OnInputEvent;
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextThreshold)
            {
                _nextThreshold = now + 0.5f;
                var events = EventSystem.current;
                // The window can move to another display (another backing scale): measured again every half second.
                // Windowed: Screen pixels over the window's content points (as WindowSize measures it, but from the game's
                // own window, found even while another app is in front); full screen: the window's backing scale.
                float ppp = FirstMouse.PixelsPerPoint();
                if (ppp > 0) PixelsPerPoint = ppp;
                else if (PixelsPerPoint <= 0) PixelsPerPoint = WindowSize.PixelsPerPoint();
                int px = ThresholdOverride ?? Mathf.Max(10, Mathf.RoundToInt(DragThresholdPoints * PixelsPerPoint));
                if (events != null && events.pixelDragThreshold != px)
                {
                    events.pixelDragThreshold = px;
                    if (Threshold != px && ThresholdOverride == null) Debug.Log($"clicks: drag threshold {px} px ({DragThresholdPoints:0} pt at {PixelsPerPoint:0.##} px per pt)");
                    Threshold = px;
                }
            }
            if (now >= _nextScan)
            {
                _nextScan = now + 0.5f;
                // Every loaded scene's roots (and this object's own, the don't-destroy one), into reused lists: no garbage.
                for (int i = 0; i <= UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                {
                    var scene = i < UnityEngine.SceneManagement.SceneManager.sceneCount ? UnityEngine.SceneManagement.SceneManager.GetSceneAt(i) : gameObject.scene;
                    if (!scene.isLoaded) continue;
                    scene.GetRootGameObjects(_roots);
                    foreach (var go in _roots)
                    {
                        go.GetComponentsInChildren(false, _scrolls);
                        foreach (var scroll in _scrolls)
                            if (!scroll.TryGetComponent<ScrollFit>(out _)) scroll.gameObject.AddComponent<ScrollFit>();
                    }
                }
                _roots.Clear();
                _scrolls.Clear();
            }
            if (!_firstMouseDone && _firstMouseTries < 600 && Time.frameCount % 10 == 0)
            {
                _firstMouseTries++;
                if (FirstMouse.Install(out var report))
                {
                    _firstMouseDone = true;
                    FirstMouseReport = report;
                    Debug.Log($"clicks: first mouse accepted by {report}");
                }
            }
        }

        private void OnFocusChanged(bool focused)
        {
            if (focused) { _focusGainedAt = Time.unscaledTime; _buttonsLogged = 0; }
            if (focused) FocusGained(); else FocusLost();
            var mice = new List<string>();
            foreach (var device in InputSystem.devices)
                if (device is Mouse m && m.name != "ControllerCursor") mice.Add($"{m.name} {(m.enabled ? "on" : "OFF")}");
            Debug.Log($"clicks: focus {(focused ? "gained" : "lost")} at {Time.realtimeSinceStartup:0.000}s; mouse {string.Join(", ", mice)}");
        }

        /// <summary>
        /// The window went to the background. The mouse and keyboard stay on (so the click that brings it back counts), but
        /// nothing stays held: a mouse button down is let go off the window (no click lands from it, the press ends, a held
        /// card lets the duel go on), the keyboards are reset (Shift held for "ask at every chance" lets go), and the
        /// controllers are switched off until the window is back, so a pad never plays the game from the background.
        /// </summary>
        public static void FocusLost()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is Mouse m && m.name != "ControllerCursor" && m.enabled
                    && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed))
                {
                    // Off the window first, then up: the release is over nothing, so it ends the press without clicking.
                    var away = new Vector2(-10000, -10000);
                    InputSystem.QueueStateEvent(m, new MouseState { position = away }
                        .WithButton(MouseButton.Left, m.leftButton.isPressed).WithButton(MouseButton.Right, m.rightButton.isPressed)
                        .WithButton(MouseButton.Middle, m.middleButton.isPressed));
                    InputSystem.QueueStateEvent(m, new MouseState { position = away });
                }
                else if (device is Keyboard k && k.enabled) InputSystem.ResetDevice(k);
                else if (device is Gamepad pad && pad.enabled)
                {
                    InputSystem.DisableDevice(pad);
                    PadsOff.Add(pad);
                }
            }
        }

        /// <summary>The window is back: the controllers switched off for the background come back on.</summary>
        public static void FocusGained()
        {
            foreach (var pad in PadsOff)
                if (pad.added && !pad.enabled) InputSystem.EnableDevice(pad);
            PadsOff.Clear();
        }

        private static readonly List<InputDevice> PadsOff = new List<InputDevice>();

        /// <summary>For 5 s after the window comes back: the real mouse's button changes (the first click must show here).</summary>
        private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device is not Mouse mouse || mouse.name == "ControllerCursor" || _buttonsLogged >= 8) return;
            if (Time.unscaledTime - _focusGainedAt > 5f) return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;
            if (!mouse.leftButton.ReadValueFromEvent(eventPtr, out var left)) return;
            bool down = left > 0.5f;
            if (down == mouse.leftButton.isPressed) return; // no change of the button
            _buttonsLogged++;
            Debug.Log($"clicks: mouse button {(down ? "down" : "up")} at {Time.realtimeSinceStartup:0.000}s, "
                      + $"{Time.unscaledTime - _focusGainedAt:0.000}s after focus; mouse {(mouse.enabled ? "on" : "OFF")}, "
                      + $"focused {Application.isFocused}");
        }

        /// <summary>
        /// AppKit's <c>acceptsFirstMouse:</c> answered YES on the window's views (through the Objective-C runtime, as
        /// WindowSize does): a click on the window while another app is in front reaches the game as well as bringing it forward.
        /// </summary>
        private static class FirstMouse
        {
            private const string Objc = "/usr/lib/libobjc.A.dylib";
            [DllImport(Objc)] private static extern IntPtr objc_getClass(string name);
            [DllImport(Objc)] private static extern IntPtr sel_registerName(string name);
            [DllImport(Objc)] private static extern IntPtr object_getClass(IntPtr obj);
            [DllImport(Objc)] private static extern IntPtr class_getName(IntPtr cls);
            [DllImport(Objc)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, ulong index);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern ulong SendCount(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern double SendDouble(IntPtr receiver, IntPtr selector);

            [StructLayout(LayoutKind.Sequential)] private struct NSRect { public double X, Y, W, H; }
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern NSRect SendRect(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(IntPtr receiver, IntPtr selector);
            [DllImport(Objc)] private static extern IntPtr class_replaceMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

            /// <summary>
            /// Screen pixels per window point: windowed, Screen.width over the content view's width in points; full screen
            /// (or no width to read), the window's backing scale. 0 when there's no game window to ask.
            /// </summary>
            public static float PixelsPerPoint()
            {
                if (Application.platform != RuntimePlatform.OSXPlayer) return 0;
                try
                {
                    var window = GameWindow(out var content);
                    if (window == IntPtr.Zero) return 0;
                    if (!Screen.fullScreen && content != IntPtr.Zero)
                    {
                        var frame = SendRect(content, sel_registerName("frame"));
                        if (frame.W > 1) return (float)(Screen.width / frame.W);
                    }
                    return (float)SendDouble(window, sel_registerName("backingScaleFactor"));
                }
                catch { return 0; }
            }

            /// <summary>The window's content width in points (0 if there's none), for checks.</summary>
            public static float ContentWidthPoints()
            {
                if (Application.platform != RuntimePlatform.OSXPlayer) return 0;
                try
                {
                    GameWindow(out var content);
                    return content == IntPtr.Zero ? 0 : (float)SendRect(content, sel_registerName("frame")).W;
                }
                catch { return 0; }
            }

            /// <summary>
            /// The game's window: the first visible one whose content view is a class of its own (Unity's view, not a plain
            /// NSView: a panel or an alert would be one), whether or not the app is in front. Zero when there's none.
            /// </summary>
            private static IntPtr GameWindow(out IntPtr content)
            {
                content = IntPtr.Zero;
                var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (app == IntPtr.Zero) return IntPtr.Zero;
                var windows = Send(app, sel_registerName("windows"));
                if (windows == IntPtr.Zero) return IntPtr.Zero;
                ulong count = SendCount(windows, sel_registerName("count"));
                for (ulong i = 0; i < count && i < 32; i++)
                {
                    var window = SendIndex(windows, sel_registerName("objectAtIndex:"), i);
                    if (window == IntPtr.Zero || !SendBool(window, sel_registerName("isVisible"))) continue;
                    var view = Send(window, sel_registerName("contentView"));
                    if (view == IntPtr.Zero) continue;
                    if ((Marshal.PtrToStringAnsi(class_getName(object_getClass(view))) ?? "NSView") == "NSView") continue;
                    content = view;
                    return window;
                }
                return IntPtr.Zero;
            }

            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate byte AcceptsFirstMouseFn(IntPtr self, IntPtr cmd, IntPtr theEvent);
            private static readonly AcceptsFirstMouseFn Yes = AcceptsFirstMouse;
            private static IntPtr _imp;
            private static readonly HashSet<IntPtr> Done = new HashSet<IntPtr>();

            [AOT.MonoPInvokeCallback(typeof(AcceptsFirstMouseFn))]
            private static byte AcceptsFirstMouse(IntPtr self, IntPtr cmd, IntPtr theEvent) => 1;

            /// <summary>
            /// Gives each view class in the game window (not plain NSView) the answer: added where the class inherits it,
            /// replaced where the class has its own. True only once a class actually got it (until then, tried again).
            /// </summary>
            public static bool Install(out string report)
            {
                report = "";
                if (Application.platform != RuntimePlatform.OSXPlayer) { report = "(not the macOS player)"; return true; }
                try
                {
                    var window = GameWindow(out var content);
                    if (window == IntPtr.Zero) return false;
                    if (_imp == IntPtr.Zero) _imp = Marshal.GetFunctionPointerForDelegate(Yes);
                    // BOOL is a C bool on arm64 ("B"), a signed char on x86_64 ("c").
                    string types = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "B@:@" : "c@:@";
                    var names = new List<string>();
                    var selector = sel_registerName("acceptsFirstMouse:");
                    var stack = new Stack<(IntPtr view, int depth)>();
                    stack.Push((content, 0));
                    while (stack.Count > 0)
                    {
                        var (view, depth) = stack.Pop();
                        var cls = object_getClass(view);
                        var name = Marshal.PtrToStringAnsi(class_getName(cls)) ?? "?";
                        if (name != "NSView" && !Done.Contains(cls))
                        {
                            if (class_addMethod(cls, selector, _imp, types)) { Done.Add(cls); names.Add(name); }
                            else
                            {
                                // The class answers itself (NO, presumably): its answer is replaced.
                                class_replaceMethod(cls, selector, _imp, types);
                                Done.Add(cls);
                                names.Add($"{name} (replaced)");
                            }
                        }
                        if (depth >= 4) continue;
                        var subviews = Send(view, sel_registerName("subviews"));
                        if (subviews == IntPtr.Zero) continue;
                        ulong count = SendCount(subviews, sel_registerName("count"));
                        for (ulong i = 0; i < count && i < 16; i++) stack.Push((SendIndex(subviews, sel_registerName("objectAtIndex:"), i), depth + 1));
                    }
                    report = string.Join(", ", names);
                    return names.Count > 0;
                }
                catch (Exception e)
                {
                    report = e.Message;
                    return false;
                }
            }
        }

        /// <summary>For checks: the window's content width in points (0 when it can't be read).</summary>
        public static float ContentWidthPoints() => FirstMouse.ContentWidthPoints();
    }

    /// <summary>
    /// A list that fits doesn't scroll: its ScrollRect is switched off, so a press on a row that moves a little is still a
    /// click (a ScrollRect takes any movement past the threshold as a drag, whether or not there's anything to scroll).
    /// It comes back on as soon as the rows outgrow the view. Added to every ScrollRect by <see cref="ClickSupport"/>.
    /// </summary>
    public sealed class ScrollFit : MonoBehaviour
    {
        private ScrollRect? _scroll;
        private Vector2 _lastContent, _lastView;
        private int _fitFrames;
        private bool _offByUs;

        /// <summary>For checks: whether this list is switched off as fitting.</summary>
        public bool Off => _offByUs;

        private void Awake() => _scroll = GetComponent<ScrollRect>();

        private void LateUpdate()
        {
            if (_scroll == null || _scroll.content == null) return;
            if (!ClickSupport.FitScrolls) { TurnOn(); return; }
            var viewRt = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;
            var view = viewRt.rect.size;
            var content = _scroll.content.rect.size * (Vector2)_scroll.content.localScale;
            bool fits = (!_scroll.vertical || content.y <= view.y + 0.5f) && (!_scroll.horizontal || content.x <= view.x + 0.5f);
            bool changed = (content - _lastContent).sqrMagnitude > 0.01f || (view - _lastView).sqrMagnitude > 0.01f;
            _lastContent = content;
            _lastView = view;
            if (!fits || changed)
            {
                // Back on for at least a few frames, so the list settles (clamps its rows into place) before it's off again.
                _fitFrames = 0;
                TurnOn();
                return;
            }
            if (!_scroll.enabled) return; // switched off by its screen: not ours to touch
            if (++_fitFrames >= 3 && _scroll.velocity.sqrMagnitude < 0.01f)
            {
                _scroll.enabled = false;
                _offByUs = true;
            }
        }

        private void TurnOn()
        {
            if (!_offByUs || _scroll == null) return;
            _scroll.enabled = true;
            _offByUs = false;
        }

        private void OnDisable() => TurnOn();
    }

    /// <summary>
    /// The same pick clicked again within a moment (a long frame, a bounce): the repeat is dropped. A pick is known by its
    /// card (controller, place, sequence, card) and the kind of decision, not by the decision itself, so it holds across
    /// an incremental pick (a new decision after each card picked). The window runs from the click that was taken: the
    /// repeats it drops don't stretch it.
    /// </summary>
    public static class ClickGuard
    {
        private static string? _key;
        private static float _at = -10f;

        public const float Window = 0.25f;

        /// <summary>True when choice <paramref name="index"/> of <paramref name="set"/> is the pick just taken, again within <see cref="Window"/> s.</summary>
        public static bool Repeat(Ygo.Engine.Interactive.ChoiceSet set, int index)
        {
            if (index < 0 || index >= set.Choices.Count) return false;
            var choice = set.Choices[index];
            string kind = set.Prompt?.GetType().Name ?? "?";
            string key = choice.Card is { } c ? $"{kind}|{c.Controller}|{c.Location & 0x7f}|{c.Sequence}|{c.Code}" : $"{kind}|{choice.Label}";
            float now = Time.realtimeSinceStartup;
            if (key == _key && now - _at < Window) return true; // dropped: the window stays where it was
            _key = key;
            _at = now;
            return false;
        }
    }
}
#endif
