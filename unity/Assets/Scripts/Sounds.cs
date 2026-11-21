using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Ygo.Engine.Audio;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Plays the game's synthesized sounds (<see cref="Sfx"/>): effects through a pool of voices (one sound each), music as
    /// tracks that crossfade. Effect clips are built once at startup. Volumes are saved settings in groups: Master over
    /// Sound effects, Music and Interface sounds (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md). Tonal effects are tuned to the
    /// music's key. The same effect can't stack within 50 ms, and repeats in quick succession get quieter, so long combos
    /// don't hammer one sound. Music dips under the moments that matter (negations, big hits, the end) and under a pause.
    /// The whole output goes through a limiter and the background fade (<see cref="OutputLimiter"/>).
    /// Duels play a layered theme (<see cref="Sfx.Themes"/>, built on a worker thread when needed): the everyday one, or
    /// a boss's (<see cref="BossMusic"/>). Its layers start together and fade in and out with the duel: the tune when the
    /// boss's ace is out, drive and dread or hope at low LP, a heartbeat alone while an attack would end the duel.
    /// The Back Alley adds its rain bed (<see cref="Bed"/>) under whatever plays while its tab or Moth's van is shown.
    /// </summary>
    /// <summary>The volume groups (docs/dev/AUDIO-PASS-1-SPEC-2026-11-20.md, section A); Master scales all three.</summary>
    public enum SoundGroup { Music, Effects, Interface }

    /// <summary>One <see cref="Sounds.Play"/> (or <see cref="Sounds.PlayLater"/> firing), for checks: what it asked, what came out.</summary>
    public readonly struct SoundLogEntry
    {
        public readonly float Time; public readonly string Name; public readonly SoundGroup Group;
        public readonly float Volume; public readonly float Pitch; public readonly bool Played; public readonly string Why;

        public SoundLogEntry(float time, string name, SoundGroup group, float volume, float pitch, bool played, string why)
        {
            Time = time; Name = name; Group = group; Volume = volume; Pitch = pitch; Played = played; Why = why;
        }

        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000} {1} {2} vol {3:0.000} pitch {4:0.0000} {5}",
                Time, Name, Group, Volume, Pitch, Played ? "played" : "not played: " + Why);
    }

    public sealed class Sounds : MonoBehaviour
    {
        private static Sounds? _instance;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        /// <summary>The effect voices: each holds one sound at a time (no PlayOneShot), so a ringing sound keeps its pitch.</summary>
        private AudioSource[] _voices = new AudioSource[0];
        private double[] _voiceStarted = new double[0];
        private bool[] _voicePrize = new bool[0];
        private const int VoiceCount = 16;

        // ---- Volumes: four groups, each a saved 0-1 (section A) ------------------------------------------------

        private static float? _masterPref, _effectsPref, _musicPref, _uiPref;
        private static (float master, float music, float effects, float ui)? _override;

        private static float Pref(ref float? cache, string key, float fallback) => cache ??= PlayerPrefs.GetFloat(key, fallback);

        /// <summary>Sets a group's volume at once; <paramref name="save"/> writes the preferences to disk too (sliders save on release).</summary>
        internal static void SetVolume(string key, float value, bool save)
        {
            value = Mathf.Clamp01(value);
            switch (key)
            {
                case "masterVolume": _masterPref = value; break;
                case "sfxVolume": _effectsPref = value; break;
                case "musicVolume": _musicPref = value; break;
                case "uiVolume": _uiPref = value; break;
                default: return;
            }
            PlayerPrefs.SetFloat(key, value);
            if (save) PlayerPrefs.Save();
        }

        /// <summary>Everything the game plays.</summary>
        public static float MasterVolume
        {
            get => _override?.master ?? Pref(ref _masterPref, "masterVolume", 1f);
            set => SetVolume("masterVolume", value, true);
        }

        /// <summary>Every effect that isn't an interface sound.</summary>
        public static float EffectsVolume
        {
            get => _override?.effects ?? Pref(ref _effectsPref, "sfxVolume", 0.8f);
            set => SetVolume("sfxVolume", value, true);
        }

        /// <summary>Button clicks, hovers and errors, and the deck editor's change sounds: on their own (Sound effects doesn't scale them).</summary>
        public static float UiVolume
        {
            get => _override?.ui ?? Pref(ref _uiPref, "uiVolume", 0.65f);
            set => SetVolume("uiVolume", value, true);
        }

        public static float MusicVolume
        {
            get => _override?.music ?? Pref(ref _musicPref, "musicVolume", 0.5f);
            set => SetVolume("musicVolume", value, true);
        }

        /// <summary>For checks: all four volumes in memory for a while (null: back to the saved ones). Never saved.</summary>
        public static void OverrideVolumes((float master, float music, float effects, float ui)? volumes) => _override = volumes;

        /// <summary>The game goes quiet while another window is in front (section D).</summary>
        public static bool MuteInBackground
        {
            get => PlayerPrefs.GetInt("muteInBackground", 1) == 1;
            set
            {
                PlayerPrefs.SetInt("muteInBackground", value ? 1 : 0);
                PlayerPrefs.Save();
                ApplyFocus(snap: !value);
            }
        }

        /// <summary>The owner's switch for hover sounds (buttons only, at half the interface volume).</summary>
        public static bool HoverSounds = true;

        /// <summary>
        /// `-mute`: the whole output is zeroed after the limiter (<see cref="OutputLimiter"/>), so checks still mix audio and
        /// read real levels; no theme is built (unless <see cref="BuildWhenMuted"/>).
        /// </summary>
        public static bool OutputMuted;

        /// <summary>
        /// Old settings, once (section A): interface sounds used to be scaled by Sound effects too, so a saved interface
        /// volume becomes what that player heard (interface x effects), rounded to the sliders' 5% steps; then the version key
        /// says it's done. Pure: the preferences in, the preferences out.
        /// </summary>
        public static IDictionary<string, float> MigratePrefs(IDictionary<string, float> prefs)
        {
            var result = new Dictionary<string, float>(prefs);
            int version = result.TryGetValue("audioPrefs", out var v) ? Mathf.RoundToInt(v) : 1;
            if (version >= 2) return result;
            if (result.TryGetValue("uiVolume", out var ui))
                result["uiVolume"] = Mathf.Round(ui * (result.TryGetValue("sfxVolume", out var sfx) ? sfx : 0.8f) * 20) / 20f;
            result["audioPrefs"] = 2;
            return result;
        }

        private static void MigrateSavedPrefs()
        {
            if (PlayerPrefs.GetInt("audioPrefs", 1) >= 2) return;
            var prefs = new Dictionary<string, float> { ["audioPrefs"] = PlayerPrefs.GetInt("audioPrefs", 1) };
            foreach (var key in new[] { "uiVolume", "sfxVolume" })
                if (PlayerPrefs.HasKey(key)) prefs[key] = PlayerPrefs.GetFloat(key);
            var migrated = MigratePrefs(prefs);
            if (migrated.TryGetValue("uiVolume", out var ui)) PlayerPrefs.SetFloat("uiVolume", ui);
            PlayerPrefs.SetInt("audioPrefs", Mathf.RoundToInt(migrated["audioPrefs"]));
            PlayerPrefs.Save();
            _uiPref = null;
        }

        private static readonly HashSet<string> UiSounds = new HashSet<string> { "click", "hover", "error" };
        // The music dips under these, so they're heard over it.
        private static readonly HashSet<string> Important = new HashSet<string> { "negate", "negate_effect", "damage_big", "win", "lose", "draw_game" };
        // A softer dip under the big reveals.
        private static readonly HashSet<string> SoftDip = new HashSet<string> { "burst", "level_up" };
        // Sounds that build or count (a chain, the LP roll, the phases): repeats don't get quieter.
        private static readonly HashSet<string> NoRepeatFade = new HashSet<string> { "activate", "chain_resolve", "lp_tick", "phase" };
        private readonly Dictionary<string, int> _repeats = new Dictionary<string, int>();
        private float _duck = 1, _duckUntil;
        /// <summary>The slight random pitch of untuned effects: its own generator (never UnityEngine.Random, which the alley's exhibitions share).</summary>
        private System.Random _jitter = new System.Random(0x50D5);

        /// <summary>Duel music that follows the duel. Off: the theme plays whole (pad, drums and tune) and steady.</summary>
        public static bool ReactiveMusic
        {
            get => ReactiveOverride ?? PlayerPrefs.GetInt("musicReactive", 1) == 1;
            set { PlayerPrefs.SetInt("musicReactive", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>For checks: build themes even when muted, to test the music at zero volume.</summary>
        public static bool BuildWhenMuted;
        /// <summary>For checks: the setting for one run (not saved), and a duel state that wins over the board's.</summary>
        public static bool? ReactiveOverride;
        public static (bool ace, int danger, bool lethal)? StateOverride;

        /// <summary>Muted (-mute) or the music off: no theme is built at all.</summary>
        private static bool Silent => !BuildWhenMuted && (MusicVolume <= 0 || MasterVolume <= 0 || OutputMuted);

        /// <summary>Builds every clip and the audio sources. Call once.</summary>
        public static void Create(GameObject host)
        {
            if (_instance != null) return;
            MigrateSavedPrefs(); // before anything plays
            // Unity only outputs sound through an AudioListener; the scene is all UI, so it has none of its own.
            // Its own GameObject: a filter on an object that also holds AudioSources is taken by them, not the listener.
            var listener = FindAnyObjectByType<AudioListener>();
            if (listener == null)
            {
                var ears = new GameObject("Listener");
                ears.transform.SetParent(host.transform, false);
                listener = ears.AddComponent<AudioListener>();
            }
            // The limiter and the background fade, on the final mix: the listener's GameObject, after it.
            if (!listener.TryGetComponent<OutputLimiter>(out _)) listener.gameObject.AddComponent<OutputLimiter>();
            _instance = host.AddComponent<Sounds>();
            _instance._jitter = new System.Random(0x50D5);
            _logging = AutomatedRun(System.Environment.GetCommandLineArgs());
            _instance.Build();
        }

        private void Build()
        {
            foreach (var pair in Sfx.Effects) _clips[pair.Key] = Clip(pair.Key, pair.Value());
            foreach (var pair in Sfx.Music) _clips["music:" + pair.Key] = Clip(pair.Key, pair.Value());
            _voices = new AudioSource[VoiceCount];
            _voiceStarted = new double[VoiceCount];
            _voicePrize = new bool[VoiceCount];
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = gameObject.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }
            _shot = gameObject.AddComponent<AudioSource>();
            _shot.playOnAwake = false;
            Prepare(BossMusic.Everyday, 0);
            AudioSettings.OnAudioConfigurationChanged += AudioConfigurationChanged;
        }

        private void OnDestroy() => AudioSettings.OnAudioConfigurationChanged -= AudioConfigurationChanged;

        /// <summary>
        /// The output device changed: Unity reset its audio and stopped every source. The music playing starts again from the
        /// top, its layers together (the rain bed restarts on its own, <see cref="UpdateBed"/>); effects are short and stay stopped.
        /// </summary>
        internal void AudioConfigurationChanged(bool deviceWasChanged)
        {
            double start = AudioSettings.dspTime + 0.1;
            foreach (var t in _tracks)
            {
                if (t.Sources.Length == 0 || t.Sources[0] == null || t.Sources[0].isPlaying) continue;
                foreach (var source in t.Sources)
                {
                    if (source == null) continue;
                    source.timeSamples = 0;
                    source.PlayScheduled(start);
                }
            }
        }

        /// <summary>For checks: as if the output device changed under every source (stopped, then the handler).</summary>
        public static void DebugDeviceChange()
        {
            var s = _instance;
            if (s == null) return;
            foreach (var t in s._tracks) foreach (var source in t.Sources) if (source != null) source.Stop();
            s.AudioConfigurationChanged(true);
        }

        private static AudioClip Clip(string name, float[] samples)
        {
            var clip = AudioClip.Create(name, samples.Length, 1, Sfx.Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// An effect built outside <see cref="Sfx.Effects"/> (the headliners' sting and the crowd, <see cref="ShowSounds"/>),
        /// played by name like the others. Built once: a second call with the same name does nothing.
        /// </summary>
        public static void AddEffect(string name, System.Func<float[]> build)
        {
            var s = _instance;
            if (s == null || s._clips.ContainsKey(name)) return;
            s._clips[name] = Clip(name, build());
        }

        /// <summary>Whether an effect by this name can play (built in, or added).</summary>
        public static bool HasEffect(string name) => _instance != null && _instance._clips.ContainsKey(name);

        /// <summary>
        /// Plays an effect by name (see <see cref="Sfx.Effects"/>). Tonal effects (<see cref="Sfx.TunedRoots"/>) are moved to the
        /// music's key; the rest take a slight random pitch for variety. Click, hover and error, and anything with
        /// <paramref name="ui"/> (the deck editor's place, draw and move), go through the interface volume; everything else
        /// through the effects volume; both through Master.
        /// </summary>
        public static void Play(string name, float volume = 1f, float pitch = 1f, bool ui = false) => _instance?.PlayNow(name, volume, pitch, ui);

        /// <summary>As <see cref="Play"/>, at exactly this pitch (no random variety): steps meant to be heard as steps (a pack's dealing).</summary>
        internal static void PlayExact(string name, float volume, float pitch) => _instance?.PlayNow(name, volume, pitch, false, jitter: false);

        /// <summary>Plays an effect <paramref name="delay"/> seconds from now (unscaled time: a pause doesn't hold it).</summary>
        public static void PlayLater(float delay, string name, float volume = 1f, float pitch = 1f, bool ui = false)
        {
            var s = _instance;
            if (s == null) return;
            if (delay <= 0) { s.PlayNow(name, volume, pitch, ui); return; }
            s._later.Add((Time.unscaledTime + delay, name, volume, pitch, ui));
        }

        private readonly List<(float at, string name, float volume, float pitch, bool ui)> _later = new List<(float, string, float, float, bool)>();

        private void PlayNow(string name, float volume, float pitch, bool ui, bool jitter = true)
        {
            var group = ui || UiSounds.Contains(name) ? SoundGroup.Interface : SoundGroup.Effects;
            var now = Time.unscaledTime;
            if (!_clips.TryGetValue(name, out var clip)) { Note(now, name, group, volume, pitch, false, "unknown"); return; }
            float groupVolume = (group == SoundGroup.Interface ? UiVolume : EffectsVolume) * MasterVolume;
            if (groupVolume <= 0) { Note(now, name, group, 0, pitch, false, "group off"); return; }
            if (_lastPlayed.TryGetValue(name, out var last) && now - last < 0.05f) { Note(now, name, group, volume * groupVolume, pitch, false, "within 50 ms"); return; }
            // The same sound again within 0.6 s: a little quieter each time (down to 40%); a pause resets it.
            int repeats = _lastPlayed.ContainsKey(name) && now - last < 0.6f ? (_repeats.TryGetValue(name, out var k) ? k + 1 : 1) : 0;
            _repeats[name] = repeats;
            if (!NoRepeatFade.Contains(name)) volume *= Mathf.Max(0.4f, Mathf.Pow(0.8f, repeats));
            _lastPlayed[name] = now;
            // The dips share one level: the lower one wins, and the later end.
            if (Important.Contains(name)) Dip(0.35f, now + 0.8f);
            else if (SoftDip.Contains(name)) Dip(0.6f, now + 1.0f);
            else if (name.StartsWith("prize_") && name != "prize_ping" && name != "prize_glint") Dip(0.7f, now + 0.5f);
            // Tonal effects sit in the music's key, with no random pitch; the rest vary a little.
            if (Sfx.TunedRoots.TryGetValue(name, out var root))
            {
                int key = MusicKey;
                if (key >= 0) pitch *= (float)Sfx.KeyRatio(root, key);
            }
            else if (jitter) pitch *= 0.97f + 0.06f * (float)_jitter.NextDouble();
            var voice = TakeVoice(out int index);
            voice.Stop();
            voice.clip = clip;
            voice.volume = volume * groupVolume;
            voice.pitch = pitch;
            voice.Play();
            _voiceStarted[index] = AudioSettings.dspTime;
            _voicePrize[index] = name.StartsWith("prize_");
            Played[name] = (Played.TryGetValue(name, out var n) ? n : 0) + 1;
            Note(now, name, group, volume * groupVolume, pitch, true, "");
        }

        private void Dip(float level, float until)
        {
            _duck = Mathf.Min(_duck, level);
            _duckUntil = Mathf.Max(_duckUntil, until);
        }

        /// <summary>
        /// The first voice that's free, else the one whose sound started earliest, sparing the prize chimes (a prized card's
        /// moment rings to its end; only when every voice holds one does the oldest go).
        /// </summary>
        private AudioSource TakeVoice(out int index)
        {
            int oldest = -1, oldestAny = 0;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (!_voices[i].isPlaying) { index = i; return _voices[i]; }
                if (_voiceStarted[i] < _voiceStarted[oldestAny]) oldestAny = i;
                if (_voicePrize[i]) continue;
                if (oldest < 0 || _voiceStarted[i] < _voiceStarted[oldest]) oldest = i;
            }
            index = oldest >= 0 ? oldest : oldestAny;
            return _voices[index];
        }

        // ---- The sound log (check runs) ------------------------------------------------------------------

        private const int LogSize = 4000;
        private static bool _logging; // an automated check: the log is kept, and the real window focus is left to DebugFocus

        /// <summary>
        /// An automated run (a `-*check`, `-fxshots`, QA play, the scripted tests): it keeps the sound log and ignores the real
        /// window focus. A launch a person drives (`-editor`, `-p0`, `-dev`...) fades in the background like the game.
        /// </summary>
        internal static bool AutomatedRun(string[] args) => args.Any(a => a.StartsWith("-") && (a.EndsWith("check", System.StringComparison.Ordinal) || AutomatedFlags.Contains(a)));

        private static readonly HashSet<string> AutomatedFlags = new HashSet<string>
        {
            "-fxshots", "-qaplay", "-qareplay", "-autoshot", "-autoshot-end", "-foilshowcase", "-gradeshowcase", "-slabreveal", "-autoclick",
            "-controllertest", "-editor-test", "-editor-dragtest",
        };
        private static readonly List<SoundLogEntry> _log = new List<SoundLogEntry>();

        /// <summary>For checks: the last 4,000 plays (check runs only; empty otherwise).</summary>
        public static IReadOnlyList<SoundLogEntry> Log => _log;

        public static void ClearLog() => _log.Clear();

        private static void Note(float time, string name, SoundGroup group, float volume, float pitch, bool played, string why)
        {
            if (!_logging) return;
            if (_log.Count >= LogSize) _log.RemoveRange(0, _log.Count - LogSize + 1);
            _log.Add(new SoundLogEntry(time, name, group, volume, pitch, played, why));
        }

        // ---- Pausing a duel, and the window in the background (sections C and D) -------------------------

        private float _pauseDip = 1, _pauseTarget = 1;

        /// <summary>A duel paused under the menu or Settings: its theme carries on at half volume (over 0.3 s), and comes back.</summary>
        public static void PauseDip(bool on)
        {
            if (_instance != null) _instance._pauseTarget = on ? 0.5f : 1f;
        }

        /// <summary>For checks: the pause dip now (1 = none, 0.5 = paused).</summary>
        public static float DebugPauseDip => _instance?._pauseDip ?? 1;

        private static bool _focused = true;

        private void OnApplicationFocus(bool focus)
        {
            // Check runs are driven by DebugFocus alone: another window coming forward mustn't silence their output.
            if (_logging) return;
            _focused = focus;
            ApplyFocus(snap: false);
        }

        private void OnApplicationPause(bool paused)
        {
            if (_logging) return;
            _focused = !paused;
            ApplyFocus(snap: false);
        }

        /// <summary>For checks: as if the window lost (false) or got back (true) the focus.</summary>
        public static void DebugFocus(bool focused)
        {
            _focused = focused;
            ApplyFocus(snap: false);
        }

        private static void ApplyFocus(bool snap) => OutputLimiter.SetTargetGain(!_focused && MuteInBackground ? 0f : 1f, snap);

        // ---- Music: tracks -----------------------------------------------------------------------

        /// <summary>Music playing: one source (the menu loop) or a theme's layers, started together so they stay in sync.</summary>
        private sealed class Track
        {
            public string Name = "";
            /// <summary>Which track this is (for checks: the same serial is the same track, never rebuilt).</summary>
            public int Serial;
            public AudioSource[] Sources = new AudioSource[0];
            public float[] Gains = new float[0];
            /// <summary>The whole track's level (crossfades), and where it's heading.</summary>
            public float Level, Target = 1;
            public bool IsTheme => Name.StartsWith("theme:");
        }

        private readonly List<Track> _tracks = new List<Track>();
        private Track? _current;
        private string _base = "";
        private AudioSource _shot = null!;
        private int _trackSerial;

        /// <summary>Switches the music ("menu", "duel" for the duel's theme, or "" for silence), crossfading over a second.</summary>
        public static void Music(string name)
        {
            var s = _instance;
            if (s == null) return;
            s._base = name;
            s.Choose();
        }

        private void Choose()
        {
            if (_base != "duel") { Switch(_base); return; }
            // The duel's theme once it's built; until then the everyday one (it's built at startup).
            Prepare(_duelTheme.theme, _duelTheme.transpose);
            Prepare(BossMusic.Everyday, 0);
            var key = Key(_duelTheme.theme, _duelTheme.transpose);
            Switch(_themes.ContainsKey(key) ? "theme:" + key : _themes.ContainsKey(Key(BossMusic.Everyday, 0)) ? "theme:" + Key(BossMusic.Everyday, 0) : "");
        }

        private void Switch(string name)
        {
            if ((_current?.Name ?? "") == name) return;
            foreach (var t in _tracks) t.Target = 0;
            _current = _tracks.Find(t => t.Name == name);
            if (_current != null) { _current.Target = 1; return; }
            if (name == "menu" && _clips.TryGetValue("music:menu", out var menu)) _current = NewTrack(name, new[] { menu });
            else if (name.StartsWith("theme:") && _themes.TryGetValue(name.Substring(6), out var theme))
                _current = NewTrack(name, Sfx.LayerNames.Select(l => theme.Layers[l]).ToArray());
            if (_current != null) _current.Gains = LayerTargets(_current);
        }

        private Track NewTrack(string name, AudioClip[] clips)
        {
            var track = new Track { Name = name, Serial = ++_trackSerial, Sources = new AudioSource[clips.Length], Gains = new float[clips.Length] };
            // Every layer starts on the same audio sample, so they loop together.
            double start = AudioSettings.dspTime + 0.1;
            for (int i = 0; i < clips.Length; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = true;
                source.volume = 0;
                source.clip = clips[i];
                source.PlayScheduled(start);
                track.Sources[i] = source;
            }
            _tracks.Add(track);
            return track;
        }

        // ---- Music: themes -----------------------------------------------------------------------

        private sealed class ThemeClips
        {
            public Dictionary<string, AudioClip> Layers = new Dictionary<string, AudioClip>();
            public AudioClip Victory = null!, Defeat = null!;
        }

        private readonly Dictionary<string, ThemeClips> _themes = new Dictionary<string, ThemeClips>();
        private readonly Dictionary<string, Task<ThemeMusic>> _rendering = new Dictionary<string, Task<ThemeMusic>>();
        private readonly HashSet<string> _failed = new HashSet<string>();
        private Task<float[]>? _sting;
        private float _stingAsked;
        private (string theme, int transpose) _duelTheme = (BossMusic.Everyday, 0);

        private static string Key(string theme, int transpose) => $"{theme}:{transpose}";

        /// <summary>Starts building a theme on a worker thread (a boss's versus screen: it's ready by the duel).</summary>
        public static void Prepare(string theme, int transpose)
        {
            var s = _instance;
            var key = Key(theme, transpose);
            if (s == null || Silent || !Sfx.Themes.TryGetValue(theme, out var t) || s._themes.ContainsKey(key) || s._rendering.ContainsKey(key) || s._failed.Contains(key)) return;
            // One boss theme at a time: drop the ones nothing needs (each is tens of MB).
            var keep = new HashSet<string> { key, Key(BossMusic.Everyday, 0), Key(s._duelTheme.theme, s._duelTheme.transpose) };
            foreach (var track in s._tracks) if (track.IsTheme) keep.Add(track.Name.Substring(6));
            foreach (var old in s._themes.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                foreach (var clip in s._themes[old].Layers.Values.Append(s._themes[old].Victory).Append(s._themes[old].Defeat)) Destroy(clip);
                s._themes.Remove(old);
            }
            s._rendering[key] = Task.Run(() => Sfx.Render(t, transpose));
        }

        /// <summary>A boss steps up: their theme's sting (quick to build), and the whole theme in the background.</summary>
        public static void Sting(string theme, int transpose)
        {
            var s = _instance;
            if (s == null || Silent || !Sfx.Themes.TryGetValue(theme, out var t)) return;
            s._sting = Task.Run(() => Sfx.RenderSting(t, transpose));
            s._stingAsked = Time.unscaledTime;
            Prepare(theme, transpose);
        }

        // ---- Music: the duel -----------------------------------------------------------------------

        private bool _ace, _lethal, _ended;
        private int _danger;
        private float _fastUntil;

        /// <summary>A duel starts: its theme (built now if it isn't yet; the everyday one plays meanwhile), from the top.</summary>
        public static void DuelTheme(string theme, int transpose)
        {
            var s = _instance;
            if (s == null) return;
            s._duelTheme = Sfx.Themes.ContainsKey(theme) ? (theme, transpose) : (BossMusic.Everyday, 0);
            s._ace = s._lethal = s._ended = false;
            s._danger = 0;
            Prepare(s._duelTheme.theme, s._duelTheme.transpose);
        }

        /// <summary>
        /// How the duel stands, every frame: <paramref name="ace"/> brings in the tune; <paramref name="danger"/> is 1
        /// when you're low, 2 when they are; <paramref name="lethal"/> while an attack would end the duel.
        /// </summary>
        public static void DuelState(bool ace, int danger, bool lethal)
        {
            var s = _instance;
            if (s == null || s._ended) return;
            if (StateOverride is { } forced) (ace, danger, lethal) = forced;
            if (lethal != s._lethal) s._fastUntil = Time.unscaledTime + 0.5f; // the drop, and the slam back, are quick
            s._ace = ace;
            s._danger = danger;
            s._lethal = lethal;
        }

        /// <summary>
        /// The duel's over: a boss theme stops for its victory or defeat ending (true); the everyday theme (and the alley's)
        /// carries on under the usual fanfare (false: play "win" or "lose").
        /// </summary>
        public static bool Ending(bool win)
        {
            var s = _instance;
            // The everyday themes (main street's and the alley's) carry on under the fanfare.
            if (s == null || s._duelTheme.theme == BossMusic.Everyday || s._duelTheme.theme == BossMusic.Alley) return false;
            var key = Key(s._duelTheme.theme, s._duelTheme.transpose);
            if (s._current?.Name != "theme:" + key || !s._themes.TryGetValue(key, out var theme)) return false;
            s._ended = true;
            s._fastUntil = Time.unscaledTime + 0.5f;
            s._shot.PlayOneShot(win ? theme.Victory : theme.Defeat);
            Played[win ? "music:victory" : "music:defeat"] = (Played.TryGetValue(win ? "music:victory" : "music:defeat", out var n) ? n : 0) + 1;
            return true;
        }

        /// <summary>What each layer of a track should be at (<see cref="Sfx.LayerNames"/>: bed, drums, lead, drive, dread, hope, lethal).</summary>
        private float[] LayerTargets(Track track)
        {
            if (!track.IsTheme) return new[] { 1f };
            var g = new float[Sfx.LayerNames.Length];
            if (_ended) return g;
            if (!ReactiveMusic) { g[0] = g[1] = g[2] = 1; return g; }
            if (_lethal) { g[6] = 1; return g; }
            g[0] = g[1] = 1;
            g[2] = _ace ? 1 : 0;
            g[3] = _danger > 0 ? 1 : 0;
            g[4] = _danger == 1 ? 1 : 0;
            g[5] = _danger == 2 ? 1 : 0;
            return g;
        }

        private void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            // Sounds asked for later (unscaled time: a pause doesn't hold them).
            for (int i = 0; i < _later.Count; i++)
            {
                if (_later[i].at > now) continue;
                var (_, name, volume, pitch, ui) = _later[i];
                _later.RemoveAt(i--);
                PlayNow(name, volume, pitch, ui);
            }
            // The dip holds a moment, then the music comes back over half a second.
            if (_duck < 1 && now > _duckUntil) _duck = Mathf.Min(1, _duck + dt * 1.3f);
            // A paused duel's theme: down to half, or back, over 0.3 s.
            _pauseDip = Mathf.MoveTowards(_pauseDip, _pauseTarget, dt * 0.5f / 0.3f);
            UpdateBed(dt);

            // Themes built on worker threads become clips here (Unity's API is main-thread only).
            foreach (var done in _rendering.Where(p => p.Value.IsCompleted).ToList())
            {
                string key = done.Key;
                _rendering.Remove(key);
                if (done.Value.IsFaulted)
                {
                    _failed.Add(key);
                    Debug.LogError($"Music theme {key} failed to build: {done.Value.Exception?.GetBaseException().Message}");
                    continue;
                }
                var music = done.Value.Result;
                var clips = new ThemeClips { Victory = Clip(key + ":victory", music.Victory), Defeat = Clip(key + ":defeat", music.Defeat) };
                foreach (var layer in music.Layers) clips.Layers[layer.Key] = Clip($"{key}:{layer.Key}", layer.Value);
                _themes[key] = clips;
                if (_base == "duel") Choose();
            }
            if (_sting is { IsCompleted: true } sting)
            {
                _sting = null;
                // Only while it still fits the moment (the versus screen just opened).
                if (!sting.IsFaulted && now - _stingAsked < 2f)
                {
                    var clip = Clip("sting", sting.Result);
                    _shot.PlayOneShot(clip);
                    _duck = 0.35f;
                    _duckUntil = now + 1.5f; // the menu music steps back for it
                    Played["music:sting"] = (Played.TryGetValue("music:sting", out var n) ? n : 0) + 1;
                    Destroy(clip, clip.length + 0.5f);
                }
            }

            // Tracks crossfade over a second; a theme's layers over a second too (two with reduced motion: no sudden
            // changes), but quickly into and out of a lethal attack and under the ending.
            float layerSpeed = FxSettings.ReducedMotion ? 0.5f : now < _fastUntil ? 6f : 1f;
            if (_current != null && _current.IsTheme) _current.Gains = MoveTowards(_current.Gains, LayerTargets(_current), dt * layerSpeed);
            float musicLevel = MasterVolume * MusicVolume;
            _shot.volume = musicLevel;
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                var t = _tracks[i];
                t.Level = Mathf.MoveTowards(t.Level, t.Target, dt);
                if (t.Level <= 0 && t.Target <= 0)
                {
                    foreach (var source in t.Sources) Destroy(source);
                    _tracks.RemoveAt(i);
                    continue;
                }
                for (int l = 0; l < t.Sources.Length; l++) t.Sources[l].volume = musicLevel * _duck * _pauseDip * t.Level * t.Gains[l];
            }
        }

        private static float[] MoveTowards(float[] from, float[] to, float step)
        {
            var next = new float[to.Length];
            for (int i = 0; i < to.Length; i++) next[i] = Mathf.MoveTowards(i < from.Length ? from[i] : 0, to[i], step);
            return next;
        }

        // ---- The alley's rain bed ----------------------------------------------------------------------

        /// <summary>The bed's level against the music volume (docs/dev/ALLEY-SPEC-2026-11-01.md section 13).</summary>
        public const float BedVolume = 0.35f;
        /// <summary>The bed fades in and out with the alley's 300 ms crossfade.</summary>
        private const float BedFade = 0.3f;

        private AudioSource? _bed;
        private Task<float[]>? _bedRender;
        private bool _bedWanted;
        private float _bedLevel;

        /// <summary>
        /// The rain bed (<see cref="Sfx.RainBed"/>: rain, a sub-bass drone and crackle, looping) on or off, fading over 300 ms
        /// (at once under Reduced motion). Built on a worker thread the first time it's wanted; nothing is built while muted.
        /// </summary>
        public static void Bed(bool on)
        {
            var s = _instance;
            if (s == null) return;
            s._bedWanted = on;
            if (on && s._bed == null && s._bedRender == null && !Silent) s._bedRender = Task.Run(Sfx.RainBed);
        }

        private void UpdateBed(float dt)
        {
            if (_bedRender is { IsCompleted: true } render)
            {
                _bedRender = null;
                if (render.IsFaulted) Debug.LogError($"The rain bed failed to build: {render.Exception?.GetBaseException().Message}");
                else
                {
                    _bed = gameObject.AddComponent<AudioSource>();
                    _bed.playOnAwake = false;
                    _bed.loop = true;
                    _bed.volume = 0;
                    _bed.clip = Clip("rain-bed", render.Result);
                }
            }
            if (_bed == null) return;
            _bedLevel = FxSettings.ReducedMotion ? (_bedWanted ? 1 : 0) : Mathf.MoveTowards(_bedLevel, _bedWanted ? 1 : 0, dt / BedFade);
            if (_bedLevel > 0 && !_bed.isPlaying) _bed.Play();
            else if (_bedLevel <= 0 && _bed.isPlaying) _bed.Stop();
            _bed.volume = MasterVolume * MusicVolume * BedVolume * _bedLevel;
        }

        // ---- Checks ----------------------------------------------------------------------------------

        /// <summary>For checks: the bed wanted, its level (0-1), whether it's built and playing.</summary>
        public static (bool wanted, float level, bool built, bool playing) DebugBed() =>
            _instance == null ? (false, 0, false, false)
                : (_instance._bedWanted, _instance._bedLevel, _instance._bed != null, _instance._bed != null && _instance._bed.isPlaying);

        /// <summary>For checks: the music's dip (1 = none).</summary>
        public static float Duck => _instance?._duck ?? 1;

        /// <summary>
        /// The key of the music you're hearing (pitch class, C = 0): the duel theme playing ("theme:name:transpose"), the menu
        /// loop's (A), or -1 when nothing tonal plays (tuned effects then play at their own pitch).
        /// </summary>
        public static int MusicKey
        {
            get
            {
                // Nothing heard (Music or Master at 0; -mute doesn't count: a muted check reports what the player would hear).
                if (MusicVolume <= 0 || MasterVolume <= 0) return -1;
                var name = _instance?._current?.Name ?? "";
                if (name == "menu") return Sfx.MenuKey;
                if (!name.StartsWith("theme:")) return -1;
                var parts = name.Substring(6).Split(':');
                if (parts.Length < 2 || !Sfx.Themes.TryGetValue(parts[0], out var theme) || !int.TryParse(parts[1], out var transpose)) return -1;
                return ((theme.Key + transpose) % 12 + 12) % 12;
            }
        }

        /// <summary>For checks: whether the duel music is in its low-LP state.</summary>
        public static bool DangerOn => _instance != null && _instance._danger > 0 && _instance._current?.IsTheme == true;

        /// <summary>For automated checks: how often each effect has played.</summary>
        public static readonly Dictionary<string, int> Played = new Dictionary<string, int>();

        /// <summary>For automated checks: clips built, and the music playing.</summary>
        public static (int clips, string music, bool playing, bool listening) DebugState() =>
            _instance == null ? (0, "", false, false)
                : (_instance._clips.Count, _instance._current?.Name ?? "", _instance._current?.Sources.All(s => s.isPlaying) == true,
                   FindAnyObjectByType<AudioListener>() is AudioListener l && l.isActiveAndEnabled && !OutputMuted);

        /// <summary>For checks: the music track playing, its serial (a new track gets a new one), and where its first layer is.</summary>
        public static (string track, int serial, int timeSamples) DebugTrack()
        {
            var t = _instance?._current;
            return t == null ? ("", 0, 0) : (t.Name, t.Serial, t.Sources.Length > 0 && t.Sources[0] != null ? t.Sources[0].timeSamples : 0);
        }

        /// <summary>For checks: each effect voice's clip, pitch, and whether it's sounding.</summary>
        public static IReadOnlyList<(string clip, float pitch, bool playing)> DebugVoices() =>
            _instance == null ? new (string, float, bool)[0]
                : _instance._voices.Select(v => (v.clip != null ? v.clip.name : "", v.pitch, v.isPlaying)).ToArray();

        /// <summary>For checks: the track playing, its layers' levels, the duel's theme, and the themes built or building.</summary>
        public static (string track, float[] gains, string duelTheme, string[] built, string[] building) DebugMusic() =>
            _instance == null ? ("", new float[0], "", new string[0], new string[0])
                : (_instance._current?.Name ?? "", _instance._current?.Gains.ToArray() ?? new float[0], Key(_instance._duelTheme.theme, _instance._duelTheme.transpose),
                   _instance._themes.Keys.ToArray(), _instance._rendering.Keys.ToArray());
    }
}
