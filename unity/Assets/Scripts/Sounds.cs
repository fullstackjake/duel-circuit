using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Ygo.Engine.Audio;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Plays the game's synthesized sounds (<see cref="Sfx"/>): effects through a small pool of voices, music as tracks
    /// that crossfade. Effect clips are built once at startup. Volumes are saved settings (effects, music, and
    /// interface clicks apart). The same effect can't stack within 50 ms, and repeats in quick succession get quieter,
    /// so long combos don't hammer one sound. Music dips under the moments that matter (negations, big hits, the end).
    /// Duels play a layered theme (<see cref="Sfx.Themes"/>, built on a worker thread when needed): the everyday one, or
    /// a boss's (<see cref="BossMusic"/>). Its layers start together and fade in and out with the duel: the tune when the
    /// boss's ace is out, drive and dread or hope at low LP, a heartbeat alone while an attack would end the duel.
    /// The Back Alley adds its rain bed (<see cref="Bed"/>) under whatever plays while its tab or Moth's van is shown.
    /// </summary>
    public sealed class Sounds : MonoBehaviour
    {
        private static Sounds? _instance;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        private AudioSource[] _voices = new AudioSource[0];
        private int _nextVoice;

        public static float EffectsVolume
        {
            get => PlayerPrefs.GetFloat("sfxVolume", 0.8f);
            set { PlayerPrefs.SetFloat("sfxVolume", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        /// <summary>Button clicks, hovers and errors.</summary>
        public static float UiVolume
        {
            get => PlayerPrefs.GetFloat("uiVolume", 0.8f);
            set { PlayerPrefs.SetFloat("uiVolume", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        private static readonly HashSet<string> UiSounds = new HashSet<string> { "click", "hover", "error" };
        // The music dips under these, so they're heard over it.
        private static readonly HashSet<string> Important = new HashSet<string> { "negate", "negate_effect", "damage_big", "win", "lose" };
        private readonly Dictionary<string, int> _repeats = new Dictionary<string, int>();
        private float _duck = 1, _duckUntil;

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat("musicVolume", 0.5f);
            set { PlayerPrefs.SetFloat("musicVolume", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

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
        private static bool Silent => !BuildWhenMuted && (MusicVolume <= 0 || AudioListener.volume <= 0);

        /// <summary>Builds every clip and the audio sources. Call once.</summary>
        public static void Create(GameObject host)
        {
            if (_instance != null) return;
            // Unity only outputs sound through an AudioListener; the scene is all UI, so it has none of its own.
            if (FindAnyObjectByType<AudioListener>() == null) host.AddComponent<AudioListener>();
            _instance = host.AddComponent<Sounds>();
            _instance.Build();
        }

        private void Build()
        {
            foreach (var pair in Sfx.Effects) _clips[pair.Key] = Clip(pair.Key, pair.Value());
            foreach (var pair in Sfx.Music) _clips["music:" + pair.Key] = Clip(pair.Key, pair.Value());
            _voices = new AudioSource[10];
            for (int i = 0; i < _voices.Length; i++)
            {
                _voices[i] = gameObject.AddComponent<AudioSource>();
                _voices[i].playOnAwake = false;
            }
            _shot = gameObject.AddComponent<AudioSource>();
            _shot.playOnAwake = false;
            Prepare(BossMusic.Everyday, 0);
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

        /// <summary>Plays an effect by name (see <see cref="Sfx.Effects"/>), with a slight random pitch for variety.</summary>
        public static void Play(string name, float volume = 1f, float pitch = 1f)
        {
            var s = _instance;
            if (s == null || !s._clips.TryGetValue(name, out var clip) || EffectsVolume <= 0) return;
            var now = Time.unscaledTime;
            if (s._lastPlayed.TryGetValue(name, out var last) && now - last < 0.05f) return;
            // The same sound again within 0.6 s: a little quieter each time (down to 40%); a pause resets it.
            int repeats = s._lastPlayed.ContainsKey(name) && now - last < 0.6f ? (s._repeats.TryGetValue(name, out var k) ? k + 1 : 1) : 0;
            s._repeats[name] = repeats;
            volume *= Mathf.Max(0.4f, Mathf.Pow(0.8f, repeats));
            s._lastPlayed[name] = now;
            if (Important.Contains(name)) { s._duck = 0.35f; s._duckUntil = now + 0.8f; }
            if (UiSounds.Contains(name)) volume *= UiVolume;
            var voice = s._voices[s._nextVoice];
            s._nextVoice = (s._nextVoice + 1) % s._voices.Length;
            // The prized chimes are tuned to the music, so they don't take the usual slight random pitch.
            bool prize = name.StartsWith("prize_");
            voice.pitch = prize ? pitch * KeyRatio : pitch * Random.Range(0.97f, 1.03f);
            if (prize && name != "prize_ping" && name != "prize_glint") { s._duck = Mathf.Min(s._duck, 0.7f); s._duckUntil = Mathf.Max(s._duckUntil, now + 0.5f); }
            voice.PlayOneShot(clip, volume * EffectsVolume);
            Played[name] = (Played.TryGetValue(name, out var n) ? n : 0) + 1;
        }

        // ---- Music: tracks -----------------------------------------------------------------------

        /// <summary>Music playing: one source (the menu loop) or a theme's layers, started together so they stay in sync.</summary>
        private sealed class Track
        {
            public string Name = "";
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
            var track = new Track { Name = name, Sources = new AudioSource[clips.Length], Gains = new float[clips.Length] };
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
            // The dip holds a moment, then the music comes back over half a second.
            if (_duck < 1 && now > _duckUntil) _duck = Mathf.Min(1, _duck + dt * 1.3f);
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
            _shot.volume = MusicVolume;
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
                for (int l = 0; l < t.Sources.Length; l++) t.Sources[l].volume = MusicVolume * _duck * t.Level * t.Gains[l];
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
            _bed.volume = MusicVolume * BedVolume * _bedLevel;
        }

        // ---- Checks ----------------------------------------------------------------------------------

        /// <summary>For checks: the bed wanted, its level (0-1), whether it's built and playing.</summary>
        public static (bool wanted, float level, bool built, bool playing) DebugBed() =>
            _instance == null ? (false, 0, false, false)
                : (_instance._bedWanted, _instance._bedLevel, _instance._bed != null, _instance._bed != null && _instance._bed.isPlaying);

        /// <summary>For checks: the music's dip (1 = none).</summary>
        public static float Duck => _instance?._duck ?? 1;

        /// <summary>
        /// The key of the music you're hearing (pitch class, C = 0): the duel theme playing ("theme:name:transpose"), else
        /// E, the everyday theme's.
        /// </summary>
        public static int MusicKey
        {
            get
            {
                var name = _instance?._current?.Name ?? "";
                if (!name.StartsWith("theme:")) return 4;
                var parts = name.Substring(6).Split(':');
                if (parts.Length < 2 || !Sfx.Themes.TryGetValue(parts[0], out var theme) || !int.TryParse(parts[1], out var transpose)) return 4;
                return ((theme.Key + transpose) % 12 + 12) % 12;
            }
        }

        /// <summary>The pitch that moves the chimes (built on E) to the music's key, by the nearest way (-6..+5 semitones).</summary>
        public static float KeyRatio => Mathf.Pow(2, (((MusicKey - 4 + 6) % 12 + 12) % 12 - 6) / 12f);

        /// <summary>For checks: whether the duel music is in its low-LP state.</summary>
        public static bool DangerOn => _instance != null && _instance._danger > 0 && _instance._current?.IsTheme == true;

        /// <summary>For automated checks: how often each effect has played.</summary>
        public static readonly Dictionary<string, int> Played = new Dictionary<string, int>();

        /// <summary>For automated checks: clips built, and the music playing.</summary>
        public static (int clips, string music, bool playing, bool listening) DebugState() =>
            _instance == null ? (0, "", false, false)
                : (_instance._clips.Count, _instance._current?.Name ?? "", _instance._current?.Sources.All(s => s.isPlaying) == true,
                   FindAnyObjectByType<AudioListener>() is AudioListener l && l.isActiveAndEnabled && AudioListener.volume > 0);

        /// <summary>For checks: the track playing, its layers' levels, the duel's theme, and the themes built or building.</summary>
        public static (string track, float[] gains, string duelTheme, string[] built, string[] building) DebugMusic() =>
            _instance == null ? ("", new float[0], "", new string[0], new string[0])
                : (_instance._current?.Name ?? "", _instance._current?.Gains.ToArray() ?? new float[0], Key(_instance._duelTheme.theme, _instance._duelTheme.transpose),
                   _instance._themes.Keys.ToArray(), _instance._rendering.Keys.ToArray());
    }
}
