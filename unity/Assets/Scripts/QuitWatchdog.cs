using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Makes sure the game's process ends when the game quits (Windows player only). On 2026-11-11 two copies of the
    /// Windows game closed their window and logged a normal shutdown, yet stayed running with no window until they were
    /// ended by hand a day later; the cause wasn't found (a fresh copy quits cleanly).
    /// <para>
    /// When the game starts quitting, this sets a Windows timer that ends the process <see cref="Seconds"/> seconds later
    /// if it's still there. The timer calls kernel32's TerminateProcess directly on a system thread, so it needs nothing
    /// from the game or from Mono, whatever state they're stuck in. A normal quit is over long before it fires. Nothing
    /// the game saves is written after quitting starts (a duel in progress is recorded when it begins), and an import cut
    /// short is tidied up by the next one.
    /// </para>
    /// <para>
    /// For tests: <c>-quitwatchdogtest</c> hangs the quit on purpose (the main thread never returns from the quitting
    /// event), and <c>-quitwatchdogoff</c> leaves the timer out.
    /// </para>
    /// </summary>
    public static class QuitWatchdog
    {
        /// <summary>How long a quit may take before the process is ended.</summary>
        public const int Seconds = 10;

        private const uint WT_EXECUTEONLYONCE = 0x8;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreateTimerQueueTimer(out IntPtr timer, IntPtr queue, IntPtr callback, IntPtr parameter, uint dueTime, uint period, uint flags);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Start()
        {
#if !UNITY_EDITOR
            if (Application.platform != RuntimePlatform.WindowsPlayer) return;
            var args = Environment.GetCommandLineArgs();
            if (!args.Contains("-quitwatchdogoff")) Application.quitting += Arm;
            // After Arm, so the timer is already set when the quit hangs.
            if (args.Contains("-quitwatchdogtest"))
                Application.quitting += () => { Debug.Log("quit watchdog: test, hanging the quit on purpose"); Thread.Sleep(Timeout.Infinite); };
#endif
        }

        /// <summary>
        /// Sets the timer. Its callback is TerminateProcess itself: a timer callback is called as (parameter, fired), which
        /// TerminateProcess reads as (process, exit code); the parameter is the current-process pseudo-handle (-1). The exit
        /// code of a process ended this way is whatever the timer passed (not 0), so a hung quit can be told from a clean one.
        /// </summary>
        private static void Arm()
        {
            try
            {
                var kernel = GetModuleHandleW("kernel32.dll");
                var terminate = kernel == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(kernel, "TerminateProcess");
                if (terminate == IntPtr.Zero) { Debug.LogWarning("quit watchdog: TerminateProcess not found; not set"); return; }
                if (CreateTimerQueueTimer(out _, IntPtr.Zero, terminate, new IntPtr(-1), Seconds * 1000u, 0, WT_EXECUTEONLYONCE))
                    Debug.Log($"quit watchdog: set ({Seconds} s)");
                else
                    Debug.LogWarning($"quit watchdog: not set (error {Marshal.GetLastWin32Error()})");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"quit watchdog: not set ({e.GetType().Name})");
            }
        }
    }
}
