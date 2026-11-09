using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-musiccheck &lt;folder&gt;`: the duel music, built even when muted (run it with -mute). The everyday theme builds in
    /// the background; a boss's versus screen plays their sting and builds their theme; the boss duel plays it, its
    /// layers following each duel state (quickly into a lethal attack, slowly with reduced motion, steady with reactive
    /// music off); a real duel to the end, finishing on the theme's ending; a theme that isn't built yet takes over from
    /// the everyday one when it is; back to the menu music. Screenshots of the versus screen, the duel and the settings.
    /// </summary>
    public sealed partial class DuelController
    {
        private System.Collections.IEnumerator MusicCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"music-{name}.png"));
                yield return null;
                yield return null;
            }
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"music check: {(ok ? "ok  " : "FAIL")} {what}"); }
            float[] Gains() => Sounds.DebugMusic().gains;
            string Show(float[] g) => string.Join(" ", g.Select(x => x.ToString("F2")));
            bool Near(float[] g, float[] want, float within = 0.1f) => g.Length == want.Length && g.Zip(want, (a, b) => Mathf.Abs(a - b) <= within).All(x => x);
            float worstFrame = 0;
            System.Collections.IEnumerator Until(Func<bool> done, float seconds)
            {
                for (float end = Time.realtimeSinceStartup + seconds; !done() && Time.realtimeSinceStartup < end;)
                {
                    yield return null;
                    worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
                }
            }
            Sounds.BuildWhenMuted = true;
            yield return new WaitForSecondsRealtime(0.5f);

            // The everyday theme, on a worker thread: the game keeps running while it builds.
            float started = Time.realtimeSinceStartup;
            Sounds.Prepare(BossMusic.Everyday, 0);
            yield return Until(() => Sounds.DebugMusic().built.Contains("duel:0"), 20);
            Report($"the everyday duel theme builds in the background in {Time.realtimeSinceStartup - started:F1} s (slowest frame meanwhile {worstFrame * 1000:F0} ms)",
                Sounds.DebugMusic().built.Contains("duel:0"));

            // The Beginner Circuit's boss steps up: their sting, and their theme building for the duel.
            _campaign.Start(CampaignState.Starters[1]);
            var profile = _campaign.Profile!;
            var circuits = _campaign.Circuits;
            int last = circuits.All[0].Matches.Count - 1;
            Report($"the Beginner Circuit's last match plays the beginner theme ({BossMusic.ForMatch(circuits, 0, last)}), its first the everyday one",
                BossMusic.ForMatch(circuits, 0, last) == ("beginner", 0) && BossMusic.ForMatch(circuits, 0, 0).theme == BossMusic.Everyday);
            int stings = Sounds.Played.GetValueOrDefault("music:sting");
            ShowVersus(0, last);
            yield return Until(() => Sounds.Played.GetValueOrDefault("music:sting") > stings, 3);
            Report("the boss's versus screen plays their sting", Sounds.Played.GetValueOrDefault("music:sting") > stings);
            Report($"menu music stays on the versus screen ({Sounds.DebugMusic().track})", Sounds.DebugMusic().track == "menu");
            yield return Shot("versus");
            worstFrame = 0;
            yield return Until(() => Sounds.DebugMusic().built.Contains("beginner:0"), 15);
            Report($"their theme is built by the time you'd press Duel! (slowest frame {worstFrame * 1000:F0} ms)", Sounds.DebugMusic().built.Contains("beginner:0"));

            // The boss duel, a bot in your seat at 16 events/s (animations on: attack arrows show).
            _scenes.Close();
            speedIndex = 5;
            var match = circuits.All[0].Matches[last];
            _pickCharacter[0] = Math.Max(0, _roster.FindIndex(p => p.Id == "kit"));
            _pickDeck[0] = profile.Deck;
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == match.Character.Id));
            _pickDeck[1] = match.Deck;
            _paused = false;
            Sounds.StateOverride = (false, 0, false);
            NewDuel((0, last));
            UpdateControls();
            yield return Until(() => Sounds.DebugMusic().track == "theme:beginner:0", 3);
            yield return new WaitForSecondsRealtime(0.2f);
            Report($"the boss duel plays their theme ({Sounds.DebugMusic().track}), bed and drums only to start ({Show(Gains())})",
                Sounds.DebugMusic().track == "theme:beginner:0" && Near(Gains(), new float[] { 1, 1, 0, 0, 0, 0, 0 }));

            // Each state's layers (bed, drums, lead, drive, dread, hope, lethal).
            var states = new (string what, (bool, int, bool) state, float wait, float[] want)[]
            {
                ("their ace: the tune comes in", (true, 0, false), 1.3f, new float[] { 1, 1, 1, 0, 0, 0, 0 }),
                ("you're low: drive and dread", (true, 1, false), 1.3f, new float[] { 1, 1, 1, 1, 1, 0, 0 }),
                ("they're low instead: drive and hope", (true, 2, false), 1.3f, new float[] { 1, 1, 1, 1, 0, 1, 0 }),
                ("an attack that would end it: the heartbeat alone, within a third of a second", (true, 2, true), 0.35f, new float[] { 0, 0, 0, 0, 0, 0, 1 }),
                ("the attack's stopped: everything slams back as quickly", (true, 2, false), 0.35f, new float[] { 1, 1, 1, 1, 0, 1, 0 }),
            };
            foreach (var (what, state, wait, want) in states)
            {
                Sounds.StateOverride = state;
                yield return new WaitForSecondsRealtime(wait);
                Report($"{what} ({Show(Gains())})", Near(Gains(), want));
                if (state.Item3) yield return Shot("lethal");
            }
            yield return Shot("duel");
            // Reduced motion: the same changes, but over about two seconds, the lethal drop included.
            var reduced = FxSettings.ReducedMotionOverride;
            FxSettings.ReducedMotionOverride = true;
            Sounds.StateOverride = (true, 0, true);
            yield return new WaitForSecondsRealtime(1f);
            var slow = Gains();
            Report($"with reduced motion the lethal drop takes about two seconds (heartbeat {slow[6]:F2}, bed {slow[0]:F2} after one)",
                slow[6] > 0.3f && slow[6] < 0.75f && slow[0] > 0.25f);
            FxSettings.ReducedMotionOverride = reduced;
            // Reactive music off: the theme whole and steady whatever happens.
            Sounds.ReactiveOverride = false;
            Sounds.StateOverride = (false, 1, true);
            yield return new WaitForSecondsRealtime(2.2f);
            Report($"with reactive music off it plays steady: bed, drums and tune ({Show(Gains())})", Near(Gains(), new float[] { 1, 1, 1, 0, 0, 0, 0 }));
            Sounds.ReactiveOverride = null;
            Sounds.StateOverride = null;

            // The rest of the duel as it comes, to its end.
            bool tune = false, low = false, heartbeat = false;
            int endings = Sounds.Played.GetValueOrDefault("music:victory") + Sounds.Played.GetValueOrDefault("music:defeat");
            for (float end = Time.realtimeSinceStartup + 300; _runner != null && !_runner.IsFinished && Time.realtimeSinceStartup < end;)
            {
                var g = Gains();
                if (g.Length == 7 && g[6] > 0.5f && !heartbeat) yield return Shot("real-lethal");
                if (g.Length == 7) { tune |= g[2] > 0.5f; low |= g[3] > 0.5f; heartbeat |= g[6] > 0.5f; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(2f);
            Report($"a real duel to the end (turn {_board.Turn}, winner {_board.Winner}): the tune {(tune ? "came in" : "never")}, "
                   + $"low-LP layers {(low ? "came on" : "never")}, the heartbeat {(heartbeat ? "came on" : "never")}", _runner?.IsFinished == true);
            int endingsNow = Sounds.Played.GetValueOrDefault("music:victory") + Sounds.Played.GetValueOrDefault("music:defeat");
            Report($"it ends on the theme's {(_board.Winner == 0 ? "victory" : "defeat")}, the layers fading out ({Show(Gains())})",
                endingsNow > endings && (Sounds.DebugMusic().track != "theme:beginner:0" || Gains().All(x => x < 0.05f)));
            yield return Shot("ending");

            // A theme that isn't built when its duel starts: the everyday one plays, then it takes over. (First out of
            // whatever the campaign result opened: its story scene, the campaign screen.)
            for (int i = 0; i < 20 && _storyScreen.IsOpen; i++) { _storyScreen.DebugFinish(); yield return null; }
            _scenes.Close();
            _campaignScreen.Close();
            _pickCharacter[0] = Math.Max(0, _roster.FindIndex(p => p.Id == "kit"));
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == "nocturne"));
            _pickDeck[1] = "hat";
            _paused = false;
            NewDuel();
            Sounds.DuelTheme("nocturne", 0);
            UpdateControls();
            yield return new WaitForSecondsRealtime(0.3f);
            Report($"a theme still building: the everyday one plays meanwhile ({Sounds.DebugMusic().track})", Sounds.DebugMusic().track == "theme:duel:0");
            yield return Until(() => Sounds.DebugMusic().track == "theme:nocturne:0", 20);
            Report($"then it takes over once built ({Sounds.DebugMusic().track})", Sounds.DebugMusic().track == "theme:nocturne:0");
            yield return new WaitForSecondsRealtime(1.5f);
            var built = Sounds.DebugMusic().built;
            Report($"only the themes in use stay built ({string.Join(", ", built)})", built.Length <= 3 && built.Contains("duel:0") && built.Contains("nocturne:0"));

            // Muted, with the check's override gone: nothing builds at all.
            Sounds.BuildWhenMuted = false;
            if (AudioListener.volume <= 0)
            {
                Sounds.Prepare("seraph", 0);
                yield return null;
                Report("muted (-mute), no theme builds", !Sounds.DebugMusic().building.Contains("seraph:0") && !Sounds.DebugMusic().built.Contains("seraph:0"));
            }
            else Debug.Log("music check: (not muted: the muted case isn't checked)");

            // Back in the menus: the menu music; and the settings with the Reactive music row.
            _paused = true;
            UpdateControls();
            _settings.Open();
            yield return new WaitForSecondsRealtime(1.5f);
            Report($"the settings play the menu music ({Sounds.DebugMusic().track})", Sounds.DebugMusic().track == "menu");
            foreach (var at in new[] { 0.6f, 0.45f, 0.3f })
            {
                _settings.DebugScroll(at);
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot($"settings-{at * 100:F0}");
            }
            Debug.Log($"music check done: {bad} failure(s)");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
