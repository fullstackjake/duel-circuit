using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace YgoCampaign.Game
{
    /// <summary>
    /// App checks keep out of the player's preferences. The game keeps its settings (foils, volumes, chain prompts, playable
    /// tabs, the window's size and place) in the macOS preferences domain of its bundle id, and the engine bumps its own
    /// session keys there at every launch, before any game code runs. Check builds carry their own bundle id
    /// (com.ygocampaign.game.qa: ProjectSetup gives it to -buildout builds, tools/qa_copy.sh to a copy of the shipping app),
    /// so their runs write only their own domain and nothing here runs.
    ///
    /// A check run under the player's id (the shipping app itself) falls back to putting the domain back as it found it:
    /// one durable copy of the domain per bundle id, taken by the first check in (before the engine's writes reach the disk),
    /// with a list of the check processes alive. Overlapping checks share the copy; the last one out restores it, from a
    /// small helper that waits for the app to exit (the engine writes the window's keys on its way out) and checks the list
    /// again first. A check that finds a copy with no check alive (one crashed) restores it before it starts. The helper's
    /// pid is kept in a marker, so a later check waits for a running restore but ignores a dead one.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The flags of automated runs (besides every <c>-*check</c>).</summary>
        private static readonly string[] CheckRunFlags =
        {
            "-qaplay", "-qareplay", "-autoshot", "-autoshot-end", "-fxshots", "-foilshowcase", "-gradeshowcase", "-slabreveal", "-autoclick",
            "-controllertest", "-editor-test", "-editor-dragtest", "-editor-hand",
            "-editor", "-p0", "-p1", // DuelController.Start's automated launches (they skip the title screen)
        };

        /// <summary>Whether this launch is an automated check run.</summary>
        public static bool IsCheckRun => Environment.GetCommandLineArgs().Any(a =>
            a.StartsWith("-") && (a.EndsWith("check", StringComparison.Ordinal) || CheckRunFlags.Contains(a)));

        private const string PlayerBundleId = "com.ygocampaign.game";
        private static bool _prefsJoined;

        private static string Id => Application.identifier;
        private static string PrefsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Preferences", Id + ".plist");
        private static string Tmp(string what) => Path.Combine(Path.GetTempPath(), $"{Id}.prefs-{what}");
        private static string Snapshot => Tmp("snapshot.plist");
        /// <summary>The domain had no file when the copy was taken: restoring deletes the domain.</summary>
        private static string SnapshotNone => Tmp("snapshot.none");
        private static string LiveList => Tmp("checks");
        private static string HelperMarker => Tmp("restore.pid");
        private static string LockFile => Tmp("lock");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void JoinPrefsGuard()
        {
            if (Application.platform != RuntimePlatform.OSXPlayer || !IsCheckRun) return;
            if (Id != PlayerBundleId)
            {
                Debug.Log($"check prefs: this build's own preferences domain ({Id}); the player's ({PlayerBundleId}) is never touched");
                return;
            }
            try
            {
                using (PrefsLock())
                {
                    WaitForRunningRestore();
                    var live = ReadLive();
                    if (live.Count == 0)
                    {
                        if (File.Exists(Snapshot) || File.Exists(SnapshotNone))
                        {
                            // A copy with no check alive: the check that took it crashed or was killed. Put it back first, and keep it.
                            RestoreNow();
                            Debug.Log("check prefs: found the copy of a check that never finished; the player's preferences are back as it found them");
                        }
                        else TakeSnapshot();
                    }
                    live.Add(Process.GetCurrentProcess().Id);
                    WriteLive(live);
                }
                _prefsJoined = true;
                Application.quitting += LeavePrefsGuard;
                Debug.Log($"check prefs: running under the player's bundle id; its preferences go back as they were when the last check quits");
            }
            catch (Exception e)
            {
                Debug.Log($"check prefs: couldn't guard the preferences ({e.Message}); this run's settings stay in them");
            }
        }

        /// <summary>
        /// At quit: off the list, then a detached helper that waits for this process to end and, if no other check is alive
        /// then, imports the copy (or deletes the domain when there was none) and removes it.
        /// </summary>
        private static void LeavePrefsGuard()
        {
            if (!_prefsJoined) return;
            _prefsJoined = false;
            try
            {
                PlayerPrefs.Save();
                int pid = Process.GetCurrentProcess().Id;
                using (PrefsLock())
                {
                    WriteLive(ReadLive().Where(p => p != pid).ToList());
                    string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
                    var script = Tmp($"restore-{pid}.sh");
                    File.WriteAllText(script, string.Join("\n", new[]
                    {
                        "#!/bin/sh",
                        "trap '' HUP",
                        "exec >/dev/null 2>&1",
                        $"while kill -0 {pid} 2>/dev/null; do sleep 0.05; done",
                        $"i=0; while ! ( set -C; echo $$ > {Q(LockFile)} ) 2>/dev/null; do i=$((i+1)); [ $i -gt 200 ] && break; sleep 0.05; done",
                        "live=0",
                        $"if [ -f {Q(LiveList)} ]; then for p in $(cat {Q(LiveList)}); do kill -0 $p 2>/dev/null && live=1; done; fi",
                        "if [ $live -eq 0 ]; then",
                        $"  if [ -f {Q(Snapshot)} ]; then /usr/bin/defaults import {Q(Id)} {Q(Snapshot)}; rm -f {Q(Snapshot)};",
                        $"  elif [ -f {Q(SnapshotNone)} ]; then /usr/bin/defaults delete {Q(Id)} 2>/dev/null; rm -f {Q(SnapshotNone)}; fi",
                        $"  rm -f {Q(LiveList)}",
                        "fi",
                        $"rm -f {Q(HelperMarker)}",
                        $"rm -f {Q(LockFile)}",
                        $"rm -f {Q(script)}",
                        "",
                    }));
                    var helper = Process.Start(new ProcessStartInfo("/bin/sh", $"\"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
                    if (helper != null) File.WriteAllText(HelperMarker, helper.Id.ToString());
                }
                Debug.Log("check prefs: the preferences go back as they were once the app has quit (if no other check is running)");
            }
            catch (Exception e)
            {
                Debug.Log($"check prefs: couldn't restore the preferences ({e.Message})");
            }
        }

        /// <summary>The domain's file as it is on disk now (no later than the first check's startup), or a note that there was none.</summary>
        private static void TakeSnapshot()
        {
            File.Delete(Snapshot); // a stale one: nothing alive took it
            File.Delete(SnapshotNone);
            if (File.Exists(PrefsFile))
            {
                File.Copy(PrefsFile, Snapshot, overwrite: true);
                var started = Process.GetCurrentProcess().StartTime;
                var written = File.GetLastWriteTime(PrefsFile);
                Debug.Log($"check prefs: kept a copy of {PrefsFile} (written {written:HH:mm:ss.fff}, this run started {started:HH:mm:ss.fff}"
                          + (written > started ? "; written since this run started" : "") + ")");
            }
            else File.WriteAllText(SnapshotNone, "");
        }

        /// <summary>The copy put back now (a crashed check's), and kept for this run's own restore.</summary>
        private static void RestoreNow()
        {
            var run = File.Exists(Snapshot)
                ? new ProcessStartInfo("/usr/bin/defaults", $"import {Id} \"{Snapshot}\"")
                : new ProcessStartInfo("/usr/bin/defaults", $"delete {Id}");
            run.UseShellExecute = false;
            run.CreateNoWindow = true;
            using var p = Process.Start(run);
            p?.WaitForExit(5000);
        }

        /// <summary>A restore still running (its helper's pid alive in the marker): waited for, up to 5 s; a dead one's marker goes.</summary>
        private static void WaitForRunningRestore()
        {
            for (var until = DateTime.UtcNow.AddSeconds(5); DateTime.UtcNow < until; System.Threading.Thread.Sleep(50))
            {
                if (!File.Exists(HelperMarker)) return;
                if (!int.TryParse(File.ReadAllText(HelperMarker).Trim(), out var helper) || !Alive(helper)) { File.Delete(HelperMarker); return; }
                // The helper takes the lock to restore: let it have it while waiting.
                ReleaseLock();
                System.Threading.Thread.Sleep(50);
                TakeLock();
            }
        }

        private static List<int> ReadLive() => File.Exists(LiveList)
            ? File.ReadAllLines(LiveList).Select(l => int.TryParse(l.Trim(), out var p) ? p : 0).Where(p => p > 0 && Alive(p)).Distinct().ToList()
            : new List<int>();

        private static void WriteLive(List<int> live)
        {
            if (live.Count == 0) File.Delete(LiveList);
            else File.WriteAllLines(LiveList, live.Select(p => p.ToString()));
        }

        private static bool Alive(int pid)
        {
            try { return !Process.GetProcessById(pid).HasExited; }
            catch { return false; }
        }

        // ---- The lock: a file made exclusively (shared with the helper's shell, `set -C`); a stale one (30 s) is broken ----

        private sealed class LockScope : IDisposable { public void Dispose() => ReleaseLock(); }

        private static IDisposable PrefsLock()
        {
            TakeLock();
            return new LockScope();
        }

        private static void TakeLock()
        {
            for (var until = DateTime.UtcNow.AddSeconds(10); ; System.Threading.Thread.Sleep(50))
            {
                try
                {
                    if (File.Exists(LockFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(LockFile) > TimeSpan.FromSeconds(30)) File.Delete(LockFile);
                    using (new FileStream(LockFile, FileMode.CreateNew, FileAccess.Write)) { }
                    return;
                }
                catch (IOException) { }
                if (DateTime.UtcNow > until) return; // go on without it rather than stall the check
            }
        }

        private static void ReleaseLock()
        {
            try { File.Delete(LockFile); } catch { }
        }
    }
}
