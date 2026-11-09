using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The game's window. ProjectSetup makes it resizable; every canvas scales with Expand (and Letterbox centres tall ones).
    /// <list type="bullet">
    /// <item>The smallest window is <see cref="MinPointsW"/> x <see cref="MinPointsH"/> screen points (the same look on Retina
    /// and 1x displays): set on the window itself (AppKit's contentMinSize, through the Objective-C runtime), so a drag
    /// can't go smaller; within a display too small for it, the display's visible size.</item>
    /// <item>Its size is remembered here, in points, between launches (Unity also restores the size it last had); going
    /// windowed from full screen (Settings, "Window") opens at that size, or the first time at 80% of the display's visible
    /// height in 16:9.</item>
    /// <item>At launch, a window bigger than its display (a size kept from a bigger monitor) is brought down to that size.</item>
    /// <item>Going full screen uses the display's own resolution, so a tall window's backbuffer isn't stretched.</item>
    /// </list>
    /// Check runs skip the minimum and the launch fit: they size the window to emulate displays on purpose.
    /// </summary>
    public sealed class WindowSize : MonoBehaviour
    {
        private const string WidthKey = "windowPointsW", HeightKey = "windowPointsH";
        /// <summary>The smallest window, in screen points (smaller reads badly: the layout is 1920x1080 scaled).</summary>
        public const int MinPointsW = 1152, MinPointsH = 648;

        private int _lastW, _lastH;
        private float _changedAt = -1;
        private bool _wasFull, _minSet, _fitted;
        private static WindowSize? _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("WindowSize");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<WindowSize>();
        }

        private void Start()
        {
            _wasFull = Screen.fullScreen;
            // Once, for the record: what this Mac reports (Screen is in pixels with Retina support; AppKit in points).
            var content = Mac.ContentPoints();
            var visible = Mac.VisiblePoints();
            Debug.Log($"window: Screen {Screen.width}x{Screen.height} px, currentResolution {Screen.currentResolution.width}x{Screen.currentResolution.height}, "
                      + $"backing scale {Mac.BackingScale():0.##}, content {(content is { } c ? $"{c.x:0}x{c.y:0}" : "?")} pt, display visible {(visible is { } v ? $"{v.x:0}x{v.y:0}" : "?")} pt, "
                      + $"{PixelsPerPoint():0.##} px per pt");
        }

        /// <summary>Screen pixels per window point: measured from the window (Screen size over its content size), else the backing scale.</summary>
        public static float PixelsPerPoint()
        {
            if (!Screen.fullScreen && Mac.ContentPoints() is { } c && c.x > 1) return Screen.width / c.x;
            var scale = Mac.BackingScale();
            return scale > 0 ? (float)scale : 1f;
        }

        /// <summary>The display's visible size in points (the menu bar and Dock left out), else its resolution in points.</summary>
        private static Vector2 DisplayPoints()
        {
            if (Mac.VisiblePoints() is { } v && v.x > 1) return v;
            var r = Screen.currentResolution;
            float ppp = PixelsPerPoint();
            return new Vector2(r.width > 0 ? r.width / ppp : 1920, r.height > 0 ? r.height / ppp : 1080);
        }

        /// <summary>Full screen (at the display's resolution) to a window (at the remembered size), or back.</summary>
        public static void Toggle()
        {
            if (!Screen.fullScreen)
            {
                var r = Screen.currentResolution;
                if (r.width > 0 && r.height > 0) Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
                else Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                return;
            }
            var (w, h) = Remembered();
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
        }

        /// <summary>
        /// The window size to open at, in Screen pixels: the last one you had (points, kept between launches), else 80% of the
        /// display's visible height in 16:9 (at most 90% of its width); never under the minimum nor over the display.
        /// </summary>
        public static (int w, int h) Remembered()
        {
            var display = DisplayPoints();
            float w = PlayerPrefs.GetFloat(WidthKey, 0), h = PlayerPrefs.GetFloat(HeightKey, 0);
            if (w < MinPointsW || h < MinPointsH || w > display.x || h > display.y)
            {
                h = display.y * 0.8f;
                w = h * 16f / 9f;
                if (w > display.x * 0.9f) { w = display.x * 0.9f; h = w * 9f / 16f; }
            }
            w = Mathf.Clamp(w, Mathf.Min(MinPointsW, display.x), display.x);
            h = Mathf.Clamp(h, Mathf.Min(MinPointsH, display.y), display.y);
            float ppp = PixelsPerPoint();
            return (Mathf.RoundToInt(w * ppp), Mathf.RoundToInt(h * ppp));
        }

        private void Update()
        {
            bool full = Screen.fullScreen;
            if (full != _wasFull) { _wasFull = full; _minSet = false; } // the window is rebuilt across a mode change
            if (full) return;
            bool check = DuelController.IsCheckRun;
            if (!check && !_minSet)
            {
                // The minimum on the window: AppKit refuses a smaller drag (a display smaller than that: its visible size).
                var display = DisplayPoints();
                _minSet = Mac.SetContentMinSize(Mathf.Min(MinPointsW, display.x), Mathf.Min(MinPointsH, display.y));
            }
            if (!check && !_fitted)
            {
                // At launch: a window kept from a bigger monitor, larger than this display, comes down to a size that fits.
                _fitted = true;
                var display = DisplayPoints();
                if (Mac.ContentPoints() is { } c && (c.x > display.x + 1 || c.y > display.y + 1))
                {
                    var (fw, fh) = Remembered();
                    Screen.SetResolution(fw, fh, FullScreenMode.Windowed);
                    return;
                }
            }
            // A size that settles (half a second after a drag) is kept, in points, for the next launch and the next "Windowed".
            int w = Screen.width, h = Screen.height;
            if (w != _lastW || h != _lastH) { _lastW = w; _lastH = h; _changedAt = Time.unscaledTime; return; }
            if (_changedAt < 0 || Time.unscaledTime - _changedAt < 0.5f) return;
            _changedAt = -1;
            float ppp = PixelsPerPoint();
            float pw = w / ppp, ph = h / ppp;
            if (pw < MinPointsW - 1 || ph < MinPointsH - 1) return; // never remembered under the minimum (a check's small window)
            if (Mathf.Abs(PlayerPrefs.GetFloat(WidthKey, 0) - pw) < 1 && Mathf.Abs(PlayerPrefs.GetFloat(HeightKey, 0) - ph) < 1) return;
            PlayerPrefs.SetFloat(WidthKey, Mathf.Round(pw));
            PlayerPrefs.SetFloat(HeightKey, Mathf.Round(ph));
            PlayerPrefs.Save();
        }

        /// <summary>The key window through the Objective-C runtime (macOS player only; elsewhere every call is a no-op).</summary>
        private static class Mac
        {
            [StructLayout(LayoutKind.Sequential)] private struct NSSize { public double W, H; }
            [StructLayout(LayoutKind.Sequential)] private struct NSRect { public double X, Y, W, H; }

            private const string Objc = "/usr/lib/libobjc.A.dylib";
            [DllImport(Objc)] private static extern IntPtr objc_getClass(string name);
            [DllImport(Objc)] private static extern IntPtr sel_registerName(string name);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern void SendSize(IntPtr receiver, IntPtr selector, NSSize size);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern NSRect SendRect(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern double SendDouble(IntPtr receiver, IntPtr selector);

            private static bool On => Application.platform == RuntimePlatform.OSXPlayer;

            private static IntPtr Window()
            {
                if (!On) return IntPtr.Zero;
                try
                {
                    var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                    if (app == IntPtr.Zero) return IntPtr.Zero;
                    var window = Send(app, sel_registerName("keyWindow"));
                    if (window == IntPtr.Zero) window = Send(app, sel_registerName("mainWindow"));
                    return window;
                }
                catch { return IntPtr.Zero; }
            }

            public static bool SetContentMinSize(float w, float h)
            {
                var window = Window();
                if (window == IntPtr.Zero) return false;
                try { SendSize(window, sel_registerName("setContentMinSize:"), new NSSize { W = w, H = h }); return true; }
                catch { return false; }
            }

            /// <summary>The window's content size in points.</summary>
            public static Vector2? ContentPoints()
            {
                var window = Window();
                if (window == IntPtr.Zero) return null;
                try
                {
                    var view = Send(window, sel_registerName("contentView"));
                    if (view == IntPtr.Zero) return null;
                    var r = SendRect(view, sel_registerName("frame"));
                    return new Vector2((float)r.W, (float)r.H);
                }
                catch { return null; }
            }

            /// <summary>The window's display's visible frame (no menu bar, no Dock), in points.</summary>
            public static Vector2? VisiblePoints()
            {
                var window = Window();
                if (window == IntPtr.Zero) return null;
                try
                {
                    var screen = Send(window, sel_registerName("screen"));
                    if (screen == IntPtr.Zero) return null;
                    var r = SendRect(screen, sel_registerName("visibleFrame"));
                    return new Vector2((float)r.W, (float)r.H);
                }
                catch { return null; }
            }

            public static double BackingScale()
            {
                var window = Window();
                if (window == IntPtr.Zero) return 0;
                try { return SendDouble(window, sel_registerName("backingScaleFactor")); }
                catch { return 0; }
            }
        }
    }
}
