using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Audio;
using Ygo.Engine.Data;
using Ygo.Engine.Protocol;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-soundcheck &lt;folder&gt;` (absolute; with -mods &lt;empty folder&gt; and -mute): audio pass 1
    /// (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md, section K), proved from the sound log, the limiter's meters and the music's
    /// state, since nobody hears a check: the volume groups, the old settings' migration, the Settings sliders, hover, the
    /// pause, the background fade, the move and chain sounds, a real duel's phases, LP ticks and climbing chains, the coin
    /// toss, the draw game, packs, tuning, the voices, the limiter and the jitter's own generator. Each step fails on the
    /// build before audio pass 1. A screenshot of the Settings sound block.
    /// </summary>
    public sealed partial class DuelController
    {
        private IEnumerator SoundCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"sound check: {(ok ? "ok  " : "FAIL")} {what}"); }
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"sound-{name}.png"));
                yield return null;
            }
            IEnumerator Wait(float seconds) { yield return new WaitForSecondsRealtime(seconds); }
            IEnumerator Until(Func<bool> done, float seconds)
            {
                for (float end = Time.realtimeSinceStartup + seconds; !done() && Time.realtimeSinceStartup < end;) yield return null;
            }
            List<SoundLogEntry> Logged(string name) => Sounds.Log.Where(e => e.Name == name).ToList();
            SoundLogEntry? Last(string name) => Sounds.Log.LastOrDefault(e => e.Name == name) is { Name: not null } e ? e : (SoundLogEntry?)null;
            bool Near(float a, float b, float within = 0.0005f) => Mathf.Abs(a - b) <= within;
            string F(float x) => x.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            string Show(SoundLogEntry? e) => e?.ToString() ?? "(none)";

            yield return Wait(1.5f);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mods") < 0)
            {
                Report("needs -mods <empty folder>", false);
                Application.Quit();
                yield break;
            }
            Debug.Log($"sound check: window {Screen.width}x{Screen.height}, output {AudioSettings.outputSampleRate} Hz {AudioSettings.speakerMode}, muted {Sounds.OutputMuted}");
            // The duel the game starts with waits; the themes build though muted.
            _paused = true;
            UpdateControls();
            Sounds.BuildWhenMuted = true;
            yield return Wait(1f);

            // 1. Groups: Interface apart from Sound effects, both under Master.
            Sounds.OverrideVolumes((1, 0.5f, 0, 1));
            Sounds.ClearLog();
            Sounds.Play("click", 0.6f);
            Sounds.Play("summon");
            var click = Last("click");
            var summon = Last("summon");
            Report($"Effects 0, Interface 1: click plays at 0.6 in Interface ({Show(click)}); summon doesn't ({Show(summon)})",
                click is { Played: true, Group: SoundGroup.Interface } c1 && Near(c1.Volume, 0.6f, 0.001f) && summon is { Played: false, Why: "group off" });
            yield return Wait(0.7f);
            Sounds.OverrideVolumes((1, 0.5f, 1, 0));
            Sounds.ClearLog();
            Sounds.Play("click", 0.6f);
            Sounds.Play("summon");
            click = Last("click");
            summon = Last("summon");
            Report($"Effects 1, Interface 0: summon plays in Effects ({Show(summon)}); click doesn't ({Show(click)})",
                summon is { Played: true, Group: SoundGroup.Effects } s1 && Near(s1.Volume, 1f, 0.001f) && click is { Played: false, Why: "group off" });
            yield return Wait(0.7f);
            Sounds.OverrideVolumes((0, 0.5f, 1, 1));
            Sounds.ClearLog();
            Sounds.Play("click", 0.6f);
            Sounds.Play("summon");
            Report($"Master 0: nothing plays ({string.Join("; ", Sounds.Log.Select(e => e.ToString()))})",
                Sounds.Log.Count == 2 && Sounds.Log.All(e => !e.Played && e.Why == "group off"));
            Sounds.OverrideVolumes(null);

            // 2. The old settings' migration (pure).
            string Dict(IDictionary<string, float> d) => "{" + string.Join(", ", d.OrderBy(p => p.Key).Select(p => $"{p.Key} {F(p.Value)}")) + "}";
            var migrated = Sounds.MigratePrefs(new Dictionary<string, float> { ["uiVolume"] = 0.8f, ["sfxVolume"] = 0.8f });
            var again = Sounds.MigratePrefs(migrated);
            var empty = Sounds.MigratePrefs(new Dictionary<string, float>());
            Report($"migration (to the 5% grid): {{uiVolume 0.8, sfxVolume 0.8}} gives {Dict(migrated)}; again {Dict(again)}; {{}} gives {Dict(empty)}",
                Near(migrated["uiVolume"], 0.65f, 0.0001f) && Near(migrated["sfxVolume"], 0.8f) && migrated["audioPrefs"] == 2 && migrated.Count == 3
                && again.Count == 3 && again.All(p => Near(p.Value, migrated[p.Key], 0)) && empty.Count == 1 && empty.TryGetValue("audioPrefs", out var ap) && ap == 2);

            // 3. The Settings sliders (on this build's own preferences domain; the values it found go back after).
            var found = (Sounds.MasterVolume, Sounds.EffectsVolume, Sounds.MusicVolume, Sounds.UiVolume);
            var foundMute = Sounds.MuteInBackground;
            Sounds.MasterVolume = 1;
            Sounds.EffectsVolume = 0.8f;
            _settings.Open();
            yield return null;
            var rows = SettingsSound.DebugRowNames();
            int at = rows.IndexOf("Master volume");
            var block = at >= 0 ? rows.Skip(at).Take(7).ToList() : new List<string>();
            Report($"the sound rows in order, then Window ({string.Join(", ", block)})",
                block.SequenceEqual(new[] { "Master volume", "Sound effects", "Music", "Reactive music", "Interface sounds", "Mute in the background", "Window" }));
            _settings.DebugShowRow("Master volume");
            yield return Wait(0.3f);
            yield return Shot("settings");
            Report($"Sound effects reads {SettingsSound.DebugSliderText("effects")}", SettingsSound.DebugSliderText("effects") == "80%");
            yield return Wait(0.7f);
            Sounds.ClearLog();
            SettingsSound.DebugStepButton("effects", up: false)?.onClick.Invoke();
            summon = Last("summon");
            Report($"< once: {SettingsSound.DebugSliderText("effects")}, saved {F(PlayerPrefs.GetFloat("sfxVolume", -1))}, and the preview at 0.75 x Master ({Show(summon)})",
                SettingsSound.DebugSliderText("effects") == "75%" && Near(PlayerPrefs.GetFloat("sfxVolume", -1), 0.75f, 0.001f)
                && summon is { Played: true } s3 && Near(s3.Volume, 0.75f * Sounds.MasterVolume, 0.001f));
            SettingsSound.DebugStep("effects", 10);
            var top = SettingsSound.DebugSliderText("effects");
            SettingsSound.DebugStepButton("effects", up: true)?.onClick.Invoke();
            Report($"> from 100% stays 100% ({top} -> {SettingsSound.DebugSliderText("effects")}, {F(Sounds.EffectsVolume)})",
                top == "100%" && SettingsSound.DebugSliderText("effects") == "100%" && Near(Sounds.EffectsVolume, 1));
#if ENABLE_INPUT_SYSTEM
            var keys = InputSystem.AddDevice<Keyboard>("SoundCheckKeys");
            var master = SettingsSound.DebugSlider("master");
            if (master != null) EventSystem.current.SetSelectedGameObject(master.gameObject);
            yield return null;
            InputSystem.QueueStateEvent(keys, new KeyboardState(Key.LeftArrow));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keys, new KeyboardState());
            yield return null;
            yield return null;
            Report($"Left on the focused Master slider: {SettingsSound.DebugSliderText("master")} (saved {F(PlayerPrefs.GetFloat("masterVolume", -1))}), focus still on it",
                SettingsSound.DebugSliderText("master") == "95%" && Near(PlayerPrefs.GetFloat("masterVolume", -1), 0.95f, 0.001f)
                && master != null && EventSystem.current.currentSelectedGameObject == master.gameObject);
            // The d-pad on the focused slider: the same step.
            var pad = InputSystem.AddDevice<Gamepad>("SoundCheckPad");
            if (master != null) EventSystem.current.SetSelectedGameObject(master.gameObject);
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.DpadLeft));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState());
            yield return null;
            yield return null;
            Report($"the d-pad's left on it: {SettingsSound.DebugSliderText("master")}", SettingsSound.DebugSliderText("master") == "90%");
            EventSystem.current.SetSelectedGameObject(null);
            InputSystem.RemoveDevice(keys);
            InputSystem.RemoveDevice(pad);

            // The mouse: a press on the track jumps the value at once; it's saved when the screen closes under the held button.
            var mouse = InputSystem.AddDevice<Mouse>("SoundCheckMouse");
            Vector2 CenterOf(Component c)
            {
                var rt = (RectTransform)c.transform;
                return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            }
            IEnumerator MouseAt(Vector2 at, bool down)
            {
                InputSystem.QueueStateEvent(mouse, new MouseState { position = at }.WithButton(MouseButton.Left, down));
                yield return null;
                yield return null;
            }
            string? Saved(string key)
            {
                // What's on disk (through cfprefsd), not PlayerPrefs' memory.
                try
                {
                    var run = new System.Diagnostics.ProcessStartInfo("/usr/bin/defaults", $"read {Application.identifier} {key}")
                        { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                    using var proc = System.Diagnostics.Process.Start(run);
                    var text = proc?.StandardOutput.ReadToEnd().Trim();
                    proc?.WaitForExit(3000);
                    return text;
                }
                catch (Exception ex) { return "(" + ex.Message + ")"; }
            }
            _settings.DebugShowRow("Sound effects");
            yield return Wait(0.2f);
            var effects = SettingsSound.DebugSlider("effects");
            string savedBefore = Saved("sfxVolume") ?? "";
            if (effects != null)
            {
                yield return MouseAt(CenterOf(effects) + new Vector2(0, 40), false);
                yield return MouseAt(CenterOf(effects), false);
                yield return MouseAt(CenterOf(effects), true);
            }
            string held = SettingsSound.DebugSliderText("effects") ?? "";
            string savedHeld = Saved("sfxVolume") ?? "";
            _settings.Close();
            yield return null;
            string savedClosed = Saved("sfxVolume") ?? "";
            yield return MouseAt(new Vector2(5, 5), false);
            // (On a Mac PlayerPrefs' writes reach the preferences domain at once, so "while held" can't be told from "saved".)
            Report($"a press mid-track: {held} at once (preferences {savedBefore} before, {savedHeld} while held); after the screen closes: {savedClosed}",
                held == "50%" && savedBefore == "1" && savedClosed == "0.5");

            // Hover only when the pointer moves onto a button: not when the row is rebuilt under a resting pointer.
            _settings.Open();
            yield return null;
            _settings.DebugShowRow("Reactive music");
            yield return Wait(0.2f);
            var reactive = SettingsSound.DebugRowButton("Reactive music");
            Sounds.OverrideVolumes((1, 0.5f, 1, 1));
            Sounds.ClearLog();
            if (reactive != null)
            {
                yield return MouseAt(CenterOf(reactive) + new Vector2(0, 200), false);
                yield return MouseAt(CenterOf(reactive), false);
            }
            int movedOn = Sounds.Log.Count(e => e.Name == "hover" && e.Played);
            yield return Wait(0.7f);
            Sounds.ClearLog();
            if (reactive != null)
            {
                var at2 = CenterOf(reactive);
                yield return MouseAt(at2, true);
                yield return MouseAt(at2, false);
                yield return Wait(0.3f);
                // And back (the setting as it was), still without moving.
                var again2 = SettingsSound.DebugRowButton("Reactive music");
                if (again2 != null) { yield return MouseAt(at2, true); yield return MouseAt(at2, false); }
                yield return Wait(0.3f);
            }
            Report($"moving onto a button ticks once ({movedOn}); two clicks on a row that rebuilds under the resting pointer: {Sounds.Log.Count(e => e.Name == "click" && e.Played)} clicks, {Sounds.Log.Count(e => e.Name == "hover")} hovers",
                movedOn == 1 && Sounds.Log.Count(e => e.Name == "click" && e.Played) == 2 && Sounds.Log.All(e => e.Name != "hover"));
            yield return MouseAt(new Vector2(5, 5), false);
            InputSystem.RemoveDevice(mouse);
            Sounds.OverrideVolumes(null);
#else
            Report("the keys, d-pad and mouse on the sliders, and hover on a rebuild (this build has no Input System)", false);
#endif
            SettingsSound.DebugStep("master", -20);
            Report($"Master at 0 reads {SettingsSound.DebugSliderText("master")}", SettingsSound.DebugSliderText("master") == "Off" && Sounds.MasterVolume <= 0);
            Sounds.MasterVolume = found.Item1;
            Sounds.EffectsVolume = found.Item2;
            Sounds.MusicVolume = found.Item3;
            Sounds.UiVolume = found.Item4;
            Sounds.MuteInBackground = foundMute;

            // 4. Hover: a button under the pointer ticks (Interface, at half); a disabled button and a card tile don't.
            Sounds.OverrideVolumes((1, 0.5f, 1, 1));
            var button = SettingsSound.DebugStepButton("music", up: true);
            void Enter(GameObject go)
            {
                var data = new PointerEventData(EventSystem.current) { delta = new Vector2(3, 0) }; // the pointer moving onto it
                ExecuteEvents.Execute(go, data, ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute(go, data, ExecuteEvents.pointerExitHandler);
            }
            Sounds.ClearLog();
            if (button != null) Enter(button.gameObject);
            var hover = Last("hover");
            Report($"pointer over a Settings button: {Show(hover)}", hover is { Played: true, Group: SoundGroup.Interface } h && Near(h.Volume, 0.5f, 0.001f));
            yield return Wait(0.7f);
            var parent = button != null ? button.transform.parent : _settings.transform;
            var off = Ui.BoxButton(parent, 0, 0, 40, 40, "x", () => { }, 12);
            off.interactable = false;
            var tile = Ui.Box(parent, 50, 0, 40, 40);
            Ui.Clickable(tile, () => { }, style: HoverStyle.Tile);
            Sounds.ClearLog();
            Enter(off.gameObject);
            Enter(tile.gameObject);
            Report($"a disabled button and a card tile: no hover ({Sounds.Log.Count(e => e.Name == "hover")} logged)", Sounds.Log.All(e => e.Name != "hover"));
            Destroy(off.gameObject);
            Destroy(tile.gameObject);
            _settings.Close();
            Sounds.OverrideVolumes(null);

            // 7. Moves and chains (pure).
            var hand = new CardRef(1, 0, Location.Hand, 0);
            var field = new CardRef(1, 0, Location.MZone, 0, 0x1);
            var set = BoardFx.MoveSoundFor(hand, new CardRef(1, 0, Location.SZone, 0, 0x8), 0x40);
            var banish = BoardFx.MoveSoundFor(field, new CardRef(1, 0, Location.Removed, 0, 0x1), 0x40);
            var toGy = BoardFx.MoveSoundFor(field, new CardRef(1, 0, Location.Grave, 0, 0x1), 0x40);
            var destroyed = BoardFx.MoveSoundFor(field, new CardRef(1, 0, Location.Grave, 0, 0x1), 0x41);
            Report($"move sounds: Set {set}, banished {banish}, sent to the GY {toGy}, destroyed {destroyed}",
                set == ("set", 0.8f) && banish == ("banish", 0.8f) && toGy == ("destroy", 0.6f) && destroyed == ("destroy", 0.9f));
            var links = Enumerable.Range(1, 5).Select(BoardFx.ChainPitchStep).ToList();
            Report($"chain steps for links 1-5: {string.Join(", ", links)}; resolving 3 links: {string.Join(", ", BoardFx.ResolveSteps(3))}; 1 link: {BoardFx.ResolveSteps(1).Count} ticks",
                links.SequenceEqual(new[] { 0, 2, 7, 12, 12 }) && BoardFx.ResolveSteps(3).SequenceEqual(new[] { 7, 2, 0 }) && BoardFx.ResolveSteps(1).Count == 0);

            // 10. The draw game's sound.
            Report($"a draw plays {ResultSound(2, true)} (a win {ResultSound(0, true)}, a loss {ResultSound(1, true)})",
                ResultSound(2, true) == "draw_game" && ResultSound(0, true) == "win" && ResultSound(1, true) == "lose");

            // 11. Packs (pure) and the grading desk's 10.
            string List(IEnumerable<(string name, float delay, float volume)> s) => string.Join(" + ", s.Select(x => $"{x.name} {F(x.delay)} {F(x.volume)}"));
            var table = new (Rarity rarity, bool hit, (string, float, float)[] want)[]
            {
                (Rarity.SecretRare, false, new[] { ("flip", 0f, 0.8f) }),
                (Rarity.SuperRare, true, new[] { ("flip", 0f, 0.8f), ("rare", 0.26f, 0.7f) }),
                (Rarity.UltraRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f) }),
                (Rarity.PlatinumRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f) }),
                (Rarity.SecretRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f), ("prize_secret", 0.46f, 0.85f) }),
                (Rarity.CollectorsRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f), ("prize_secret", 0.46f, 0.85f) }),
                (Rarity.GhostGoldRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f), ("prize_ghost", 0.46f, 0.85f) }),
                (Rarity.GhostRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f), ("prize_ghost", 0.46f, 0.85f) }),
                (Rarity.QuarterCenturySecretRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f), ("prize_starlight", 0.46f, 0.85f) }),
                (Rarity.StarlightRare, true, new[] { ("flip", 0f, 0.8f), ("burst", 0.26f, 1f), ("prize_starlight", 0.46f, 0.85f) }),
            };
            foreach (var (rarity, hit, want) in table)
            {
                var got = PackSounds.RevealSounds(rarity, hit);
                Report($"a {(hit ? "hit" : "non-hit")} {rarity} turns: {List(got)}", got.Count == want.Length && got.Zip(want, (g, w) => g.name == w.Item1 && Near(g.delay, w.Item2) && Near(g.volume, w.Item3)).All(x => x));
            }
            Report($"chimes: Starlight {PackSounds.ChimeFor(Rarity.StarlightRare)}, Ultra {PackSounds.ChimeFor(Rarity.UltraRare) ?? "none"}",
                PackSounds.ChimeFor(Rarity.StarlightRare) == "prize_starlight" && PackSounds.ChimeFor(Rarity.UltraRare) == null);
            Sounds.OverrideVolumes((1, 0.5f, 1, 1));
            Sounds.ClearLog();
            float slabAt = Time.unscaledTime;
            PackSounds.SlabNumber(Rarity.SecretRare, 10);
            yield return Wait(0.7f);
            var slabSounds = Sounds.Log.Where(e => e.Played).Select(e => $"{e.Name} +{F(e.Time - slabAt)}").ToList();
            Report($"a Secret Rare's slab 10: {string.Join(", ", slabSounds)}",
                Sounds.Log.Where(e => e.Played).Select(e => e.Name).SequenceEqual(new[] { "burst", "prize_ping", "prize_secret" }));
            yield return Wait(0.7f);
            Sounds.ClearLog();
            for (int i = 0; i < 9; i++) { PackSounds.Dealt(i); yield return Wait(0.06f); }
            var deal = Sounds.Log.Where(e => e.Name == "draw" && e.Played).Select(e => e.Pitch).ToList();
            Report($"a 9-card deal's ticks rise: {string.Join(" ", deal.Select(x => x.ToString("0.000")))}",
                deal.Count == 9 && deal.Zip(deal.Skip(1), (a, b) => b > a).All(x => x));

            // 13. Voices: a ringing prize chime keeps its voice and pitch while the pool fills with long sounds and 20 clicks play.
            Sounds.ClearLog();
            Sounds.Play("prize_secret");
            var voices = Sounds.DebugVoices();
            int prize = -1;
            for (int i = 0; i < voices.Count; i++) if (voices[i].clip == "prize_secret" && voices[i].playing) prize = i;
            float prizePitch = prize >= 0 ? voices[prize].pitch : 0;
            bool kept = prize >= 0;
            void Watch()
            {
                var v = Sounds.DebugVoices();
                if (prize >= 0 && v[prize].playing && (v[prize].clip != "prize_secret" || v[prize].pitch != prizePitch)) kept = false;
            }
            // More long sounds than the pool has free voices: some voice must be taken, never the chime's.
            var longOnes = new[] { "summon_big", "win", "lose", "level_up", "burst", "rare", "draw_game", "destroy", "damage_big", "negate",
                "special", "recover", "banish", "token", "coin_spin", "summon", "pack_tear", "negate_effect" };
            foreach (var name in longOnes) { Sounds.Play(name, 0.3f); Watch(); }
            int busy = Sounds.DebugVoices().Count(v => v.playing);
            int clicks = 0, frames = 0;
            for (float start = Time.realtimeSinceStartup, end = start + 1.4f; Time.realtimeSinceStartup < end;)
            {
                // A click every 60 ms (past the 50 ms guard), so each is played, not dropped.
                if (clicks < 20 && Time.realtimeSinceStartup >= start + clicks * 0.06f) { Sounds.Play("click"); clicks++; Watch(); }
                yield return null;
                frames++;
                Watch();
            }
            int clicksPlayed = Sounds.Log.Count(e => e.Name == "click" && e.Played);
            int longPlayed = Sounds.Log.Count(e => longOnes.Contains(e.Name) && e.Played);
            Report($"{voices.Count} voices; prize_secret on voice {prize} at pitch {prizePitch:0.0000} kept while {longPlayed} long sounds filled the pool ({busy} busy) and {clicksPlayed} of {clicks} clicks played, over {frames} frames",
                voices.Count == 16 && kept && longPlayed == longOnes.Length && busy == 16 && clicks == 20 && clicksPlayed == 20);

            // 15. The jitter has its own generator: Unity's random numbers (the alley's exhibitions) are left alone.
            var state = UnityEngine.Random.state;
            for (int i = 0; i < 50; i++) Sounds.Play("summon");
            foreach (var name in new[] { "special", "attack", "impact", "move", "place", "draw", "shuffle", "damage", "cost", "negate" }) Sounds.Play(name);
            var after = UnityEngine.Random.state;
            Report("50 plays of summon (and ten other effects) leave UnityEngine.Random's state as it was", JsonUtility.ToJson(state) == JsonUtility.ToJson(after));
            Sounds.OverrideVolumes(null);

            // 9. The coin toss: the spin, the landing 1.3 s later, the result's chime 0.12 s after it; no spin under Reduced motion.
            Sounds.ClearLog();
            _scenes.ShowCoinToss("You", "Rival", youWin: true, themFirst: false, hint: "", done: _ => { });
            yield return Wait(2f);
            var spin = Last("coin_spin");
            var land = Last("coin_land");
            var chime = Last("win");
            Report($"coin toss: {Show(spin)}; {Show(land)}; {Show(chime)}",
                spin != null && land != null && chime != null && land.Value.Time - spin.Value.Time is > 1.2f and < 1.5f && chime.Value.Time - land.Value.Time is > 0.09f and < 0.2f);
            _scenes.Close();
            var reduced = FxSettings.ReducedMotionOverride;
            FxSettings.ReducedMotionOverride = true;
            yield return Wait(0.7f);
            Sounds.ClearLog();
            _scenes.ShowCoinToss("You", "Rival", youWin: false, themFirst: true, hint: "", done: _ => { });
            yield return Wait(0.6f);
            Report($"under Reduced motion: no spin ({Logged("coin_spin").Count}), the landing ({Show(Last("coin_land"))}) and turn ({Show(Last("turn"))})",
                Logged("coin_spin").Count == 0 && Last("coin_land") != null && Last("turn") != null);
            FxSettings.ReducedMotionOverride = reduced;
            _scenes.Close();

            // 5. The pause: a CPU-vs-CPU duel, Settings from its gear for 2 s: the same theme, carrying on, at half.
            Sounds.OverrideVolumes((1, 0.5f, 1, 1));
            seed++;
            PickRandomBots();
            NewDuel();
            _paused = false;
            speedIndex = 3;
            UpdateControls();
            yield return Until(() => Sounds.DebugTrack().track.StartsWith("theme:"), 25);
            yield return Wait(1.2f);
            var before = Sounds.DebugTrack();
            _view.DebugOpenSettings();
            yield return Wait(2f);
            var paused = Sounds.DebugTrack();
            Report($"paused under Settings for 2 s: {paused.track} serial {paused.serial} (was {before.track} {before.serial}), at sample {paused.timeSamples} (was {before.timeSamples}), dip {F(Sounds.DebugPauseDip)}",
                before.track.StartsWith("theme:") && paused.track == before.track && paused.serial == before.serial && paused.timeSamples != before.timeSamples
                && Near(Sounds.DebugPauseDip, 0.5f, 0.02f));
            _settings.Back();
            yield return Wait(0.5f);
            var resumed = Sounds.DebugTrack();
            Report($"back to the duel: dip {F(Sounds.DebugPauseDip)} within 0.5 s, same track ({resumed.track} serial {resumed.serial})",
                Sounds.DebugPauseDip >= 0.999f && resumed.serial == before.serial && resumed.track == before.track);
            OpenMenuFromDuel();
            yield return Wait(1.5f);
            Report($"the menu over a free duel (no way back to it) plays the menu music ({Sounds.DebugTrack().track}, dip {F(Sounds.DebugPauseDip)})",
                Sounds.DebugTrack().track == "menu" && Sounds.DebugPauseDip >= 0.999f);
            ResumeFromMenu();
            yield return Until(() => Sounds.DebugTrack().track.StartsWith("theme:"), 5);

            // 6. The window in the background.
            Sounds.MuteInBackground = true;
            Sounds.DebugFocus(false);
            yield return Wait(0.4f);
            float gone = OutputLimiter.OutputGain;
            Sounds.DebugFocus(true);
            yield return Wait(0.4f);
            float back = OutputLimiter.OutputGain;
            Sounds.MuteInBackground = false;
            Sounds.DebugFocus(false);
            yield return Wait(0.4f);
            float kept2 = OutputLimiter.OutputGain;
            Sounds.DebugFocus(true);
            Sounds.MuteInBackground = foundMute;
            Report($"another window in front: output gain {F(gone)} within 0.4 s, back to {F(back)}; with the setting off it stays {F(kept2)}",
                gone < 0.01f && back > 0.99f && kept2 > 0.99f);

            // 12. Tuning: tonal effects in the music's key, with no jitter.
            float Pitch(string name) { Sounds.Play(name); return Last(name)?.Pitch ?? -1; }
            Sounds.DuelTheme("beginner", 0);
            yield return Until(() => Sounds.DebugTrack().track == "theme:beginner:0", 25);
            Sounds.ClearLog();
            float winBeginner = Pitch("win"), prizeBeginner = Pitch("prize_secret");
            Sounds.DuelTheme("expert", 0);
            yield return Until(() => Sounds.DebugTrack().track == "theme:expert:0", 25);
            float winExpert = Pitch("win");
            _paused = true;
            UpdateControls();
            _settings.Open();
            yield return Until(() => Sounds.DebugTrack().track == "menu", 3);
            float levelUp = Pitch("level_up");
            yield return Wait(0.1f);
            Sounds.OverrideVolumes((1, 0, 1, 1));
            int keyOff = Sounds.MusicKey;
            float levelUpOff = Pitch("level_up");
            Sounds.OverrideVolumes((1, 0.5f, 1, 1));
            Report($"with Music at 0 on the menu: key {keyOff}, level_up at {levelUpOff:0.0000} (its own pitch)", keyOff == -1 && Near(levelUpOff, 1f));
            Report($"tuned: win over beginner {winBeginner:0.0000}, over expert {winExpert:0.0000}; level_up over the menu loop {levelUp:0.0000}; prize_secret over beginner {prizeBeginner:0.0000}",
                Near(winBeginner, 1f) && Near(winExpert, 1.2599f) && Near(levelUp, 0.8409f) && Near(prizeBeginner, 0.7937f));

            // 14. The limiter: the final blow's pile-up over the duel theme at full volume, then the menu loop alone.
            _settings.Close();
            Sounds.OverrideVolumes((1, 1, 1, 1));
            Sounds.ReactiveOverride = false; // the theme whole: bed, drums and tune
            yield return Until(() => Sounds.DebugTrack().track == "theme:expert:0", 3);
            yield return Wait(1.5f);
            // Where the peaks land depends on the music under them and the effects' slight random pitch: up to three
            // pile-ups, until one sums past full scale; every one must come out under -1 dBFS, nothing clamped.
            (float inputPeak, float outputPeak, float minGain, int clamped) m = (0f, 0f, 1f, 0);
            float loudestOut = 0;
            int clampedAll = 0, tries = 0;
            long limited = 0;
            for (; tries < 3 && m.inputPeak <= 1.0f; tries++)
            {
                OutputLimiter.ResetMeters();
                yield return Wait(0.05f);
                Sounds.Play("damage_big");
                Sounds.Play("impact");
                Sounds.PlayLater(0.2f, "win");
                yield return Wait(1.2f);
                m = OutputLimiter.Meters;
                loudestOut = Mathf.Max(loudestOut, m.outputPeak);
                clampedAll += m.clamped;
                limited += OutputLimiter.Frames;
            }
            Sounds.ReactiveOverride = null;
            Report($"the final blow at Master, Effects and Music 100% ({tries} pile-up(s)): in {F(m.inputPeak)}, out {F(loudestOut)} at most, lowest gain {F(m.minGain)}, clamped {clampedAll} ({limited} frames limited)",
                limited > 0 && m.inputPeak > 1.0f && loudestOut <= 0.90f && clampedAll == 0);
            Sounds.OverrideVolumes((1, 0.5f, 0, 0));
            _settings.Open();
            yield return Until(() => Sounds.DebugTrack().track == "menu", 3);
            yield return Wait(1.2f);
            OutputLimiter.ResetMeters();
            yield return Wait(2f);
            m = OutputLimiter.Meters;
            Report($"2 s of the menu loop at Music 50%: in {F(m.inputPeak)}, lowest gain {m.minGain:0.0000} (untouched)", m.inputPeak > 0.01f && m.minGain >= 0.999f);
            // Meters read right after a reset are empty, whatever buffer is in flight.
            bool emptyAfterReset = true;
            for (int i = 0; i < 30; i++)
            {
                OutputLimiter.ResetMeters();
                var r = OutputLimiter.Meters;
                if (r.inputPeak != 0 || r.outputPeak != 0 || OutputLimiter.Frames != 0) emptyAfterReset = false;
                yield return null;
            }
            Report("meters read right after a reset are empty (30 frames of music playing)", emptyAfterReset);
            Report($"the limiter is rebuilt only for a new rate or channel count: no device (rate 0) after a build at 0 {OutputLimiter.NeedsRebuild(0, 0, 0, 2)}, "
                   + $"same 48 kHz stereo {OutputLimiter.NeedsRebuild(48000, 48000, 0, 2)}, 48 kHz after 0 {OutputLimiter.NeedsRebuild(48000, 0, 0, 2)}, 6 channels seen {OutputLimiter.NeedsRebuild(48000, 48000, 6, 2)}",
                !OutputLimiter.NeedsRebuild(0, 0, 0, 2) && !OutputLimiter.NeedsRebuild(48000, 48000, 0, 2) && OutputLimiter.NeedsRebuild(48000, 0, 0, 2) && OutputLimiter.NeedsRebuild(48000, 48000, 6, 2));
            // A new output device: Unity stops every source; the music starts again.
            var beforeChange = Sounds.DebugTrack();
            Sounds.DebugDeviceChange();
            yield return Wait(0.4f);
            var afterChange = Sounds.DebugState();
            Report($"after a device change the music plays again ({afterChange.music}, {(afterChange.playing ? "playing" : "silent")}; was {beforeChange.track})",
                afterChange.playing && afterChange.music == beforeChange.track);
            _settings.Close();

            // 8. A real duel at speed 4 to its end: phases, LP ticks, the turn chime, and chains that climb.
            Sounds.OverrideVolumes((1, 0.5f, 1, 1));
            int maxChain = 0;
            Action<int> chainShown = n => maxChain = Math.Max(maxChain, n);
            _view.ChainShown += chainShown;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                maxChain = 0;
                seed++;
                PickRandomBots();
                NewDuel();
                _paused = false;
                speedIndex = 3;
                UpdateControls();
                Sounds.ClearLog();
                int phases = 0, ticks = 0, turns = 0, activates = 0, climbs = 0, longest = 0, wrong = 0, run = 0, resolves = 0;
                int lastStep = -1;
                var odd = new List<string>();
                float started = Time.realtimeSinceStartup;
                while (_runner != null && !_runner.IsFinished && Time.realtimeSinceStartup < started + 420)
                {
                    yield return null;
                    int key = Sounds.MusicKey;
                    foreach (var e in Sounds.Log)
                    {
                        if (!e.Played) continue;
                        if (e.Name == "phase") phases++;
                        else if (e.Name == "lp_tick") ticks++;
                        else if (e.Name == "turn") turns++;
                        else if (e.Name == "chain_resolve") resolves++;
                        else if (e.Name == "activate")
                        {
                            activates++;
                            double ratio = key >= 0 ? Sfx.KeyRatio(Sfx.TunedRoots["activate"], key) : 1;
                            double semis = 12 * Math.Log(e.Pitch / ratio, 2);
                            int step = (int)Math.Round(semis);
                            if (Math.Abs(semis - step) > 0.02 || !Sfx.ChainSteps.Contains(step)) { wrong++; if (odd.Count < 5) odd.Add($"{semis:0.00}"); continue; }
                            if (step == 0) run = 1;
                            else if (lastStep >= 0 && step > lastStep) { run++; climbs++; }
                            else if (!(step == 12 && lastStep == 12)) { wrong++; if (odd.Count < 5) odd.Add($"{lastStep}->{step}"); }
                            longest = Math.Max(longest, run);
                            lastStep = step;
                        }
                    }
                    Sounds.ClearLog();
                }
                bool ended = _runner != null && _runner.IsFinished;
                yield return Wait(1f);
                var what = $"a real duel (seed {seed}, attempt {attempt + 1}) {(ended ? "to its end" : "cut off at 420 s")} on turn {_board.Turn}: {phases} phase ticks, {ticks} LP ticks, "
                           + $"{turns} turn chimes, {activates} activations, {resolves} resolve ticks, longest chain shown {maxChain}, longest climb heard {longest} links ({climbs} steps up; {wrong} out of order{(odd.Count > 0 ? ": " + string.Join(", ", odd) : "")})";
                if (maxChain >= 2)
                {
                    Report(what, ended && phases >= 4 && ticks >= 3 && turns >= 1 && wrong == 0 && longest >= 2 && resolves >= 2);
                    break;
                }
                Debug.Log($"sound check: (no chain of two links: {what}; the next seed)");
                if (attempt == 2) Report("a chain of two links or more within 3 seeds", false);
            }
            _view.ChainShown -= chainShown;

            // 5b. A replay: under the menu it's left (menu music); under the gear's Settings it pauses (the theme dipped).
            var recent = _replays.All().Where(e => e.Recent).ToList();
            if (recent.Count == 0) Report("a replay to watch (the duel above saves one)", false);
            else
            {
                WatchReplay(recent[0].Replay, 0);
                _paused = false;
                speedIndex = 3;
                UpdateControls();
                yield return Until(() => Sounds.DebugTrack().track.StartsWith("theme:"), 25);
                yield return Wait(1.2f);
                OpenMenuFromDuel();
                yield return Wait(1.5f);
                Report($"a replay under the menu: the menu music ({Sounds.DebugTrack().track}, dip {F(Sounds.DebugPauseDip)})",
                    Sounds.DebugTrack().track == "menu" && Sounds.DebugPauseDip >= 0.999f);
                ResumeFromMenu();
                yield return Until(() => Sounds.DebugTrack().track.StartsWith("theme:"), 5);
                yield return Wait(1.2f);
                var replayTrack = Sounds.DebugTrack();
                _view.DebugOpenSettings();
                yield return Wait(1.5f);
                var replayPaused = Sounds.DebugTrack();
                Report($"a replay under the gear's Settings: {replayPaused.track} serial {replayPaused.serial} (was {replayTrack.serial}), dip {F(Sounds.DebugPauseDip)}",
                    replayTrack.track.StartsWith("theme:") && replayPaused.track == replayTrack.track && replayPaused.serial == replayTrack.serial && Near(Sounds.DebugPauseDip, 0.5f, 0.02f));
                _settings.Back();
                yield return Wait(0.5f);
            }

            // 5c. A campaign duel waiting behind the menu ("Back to the duel"): its theme carries on, dipped, and comes back.
            _campaign.Start(CampaignState.Starters[1]);
            var match = _campaign.Circuits.All[0].Matches[0];
            _pickCharacter[0] = Math.Max(0, _roster.FindIndex(p => p.Id == "kit"));
            _pickDeck[0] = _campaign.Profile!.Deck;
            _pickCharacter[1] = Math.Max(0, _roster.FindIndex(p => p.Id == match.Character.Id));
            _pickDeck[1] = match.Deck;
            _paused = false;
            NewDuel((0, 0));
            UpdateControls();
            yield return Until(() => Sounds.DebugTrack().track.StartsWith("theme:"), 25);
            yield return Wait(1.2f);
            var campaignTrack = Sounds.DebugTrack();
            OpenMenuFromDuel();
            yield return Wait(2f);
            var underMenu = Sounds.DebugTrack();
            Report($"a campaign duel under the menu (live {CampaignDuelLive}): {underMenu.track} serial {underMenu.serial} (was {campaignTrack.track} {campaignTrack.serial}), "
                   + $"at sample {underMenu.timeSamples} (was {campaignTrack.timeSamples}), dip {F(Sounds.DebugPauseDip)}",
                CampaignDuelLive && campaignTrack.track.StartsWith("theme:") && underMenu.track == campaignTrack.track && underMenu.serial == campaignTrack.serial
                && underMenu.timeSamples != campaignTrack.timeSamples && Near(Sounds.DebugPauseDip, 0.5f, 0.02f));
            ResumeFromMenu();
            yield return Wait(0.5f);
            Report($"back to it: dip {F(Sounds.DebugPauseDip)}, same track ({Sounds.DebugTrack().serial})",
                Sounds.DebugPauseDip >= 0.999f && Sounds.DebugTrack().serial == campaignTrack.serial);
            Sounds.OverrideVolumes(null);

            Debug.Log($"sound check done: {bad} failure(s)");
            yield return Wait(0.5f);
            Application.Quit();
        }
    }
}
