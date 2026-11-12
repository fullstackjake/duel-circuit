using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Ygo.Engine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Card pictures supplied by the player. The game ships with no card art (it belongs to Konami); instead it
    /// looks for "&lt;card id&gt;.jpg" / ".png" in, in order:
    /// <list type="number">
    /// <item>mods/cards/images/ (your own pictures)</item>
    /// <item>an EDOPro "pics" folder: set "edopro_pics: /path/to/pics" in mods/settings.txt, pass -pics &lt;path&gt;,
    /// or keep EDOPro in a common place (~/ProjectIgnis, ~/Applications/ProjectIgnis, ~/Downloads/ProjectIgnis).</item>
    /// <item>HD picture folders: mods/cards/hd (filled by Import Cards), and an "hd_pics: /path/to/pics" line in
    /// mods/settings.txt for each other (or -hdpics &lt;path&gt;, comma-separated for several, "off" for none this run).</item>
    /// </list>
    /// When a card has a picture in more than one of them, the biggest wins (by pixel size, read from the files' headers).
    /// Alternate artworks fall back to their base card's picture. Cards without a picture keep the text look. Pictures are
    /// turned into textures by <see cref="ArtLoader"/>: capped and compressed for everyday views (<see cref="Get"/>), whole
    /// for the big ones (<see cref="GetFull"/>).
    /// <para>
    /// Art styles: other pictures of the same card (OCG extended art, manga printings...) go in
    /// mods/cards/styles/&lt;style&gt;/&lt;card id&gt;.jpg / .png; every folder there is a style, named after the folder. The folders
    /// above are the Standard style. A card's Artwork button (deck editor, card reader) cycles through the styles that have a
    /// picture of it, and Settings has a preferred style for cards with no choice of their own. Choices are kept in
    /// mods/cards/art-styles.txt and show wherever the card is drawn. A picture that isn't card-shaped is letterboxed, not
    /// stretched. Original-text versions share their base card's styles; alternate artworks with a picture of their own
    /// use styles saved under their own id.
    /// </para>
    /// </summary>
    public sealed class CardArt
    {
        private const int MaxCached = 300;
        /// <summary>Whole-size pictures kept for the big views (a reader or a full-size slab shows one at a time).</summary>
        private const int MaxFull = 4;
        private static readonly string[] Extensions = { ".jpg", ".png", ".jpeg" };

        private readonly List<string> _folders = new List<string>();
        private readonly ICardDataProvider _cards;
        private readonly Dictionary<uint, Texture2D?> _cache = new Dictionary<uint, Texture2D?>();
        private readonly LinkedList<uint> _recent = new LinkedList<uint>();
        private readonly LinkedList<(uint code, Texture2D? texture)> _full = new LinkedList<(uint, Texture2D?)>();
        /// <summary>Each card's Standard picture files, the one to use first (biggest), by card id.</summary>
        private readonly Dictionary<uint, string[]> _files = new Dictionary<uint, string[]>();
        private readonly string? _edopro;

        public IReadOnlyList<string> Folders => _folders;

        /// <summary>The Standard style: the pictures in the folders above.</summary>
        public const string Standard = "";
        /// <summary>Width over height of a card picture (EDOPro's are 177x254, YGOPRODeck's 268x391).</summary>
        public const float CardAspect = 0.69f;

        /// <summary>The style folders (mods/cards/styles/*), each with the card ids it has a picture of.</summary>
        private readonly SortedDictionary<string, HashSet<uint>> _styles = new SortedDictionary<string, HashSet<uint>>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Styles chosen per card (by art key), and the style for cards with no choice.</summary>
        private readonly Dictionary<uint, string> _choices = new Dictionary<uint, string>();
        private string _preferred = Standard;

        public string StylesRoot => Path.Combine(ModsRoot, "cards", "styles");
        /// <summary>The player's own card pictures (mods/cards/images): Import Cards' Card Images.</summary>
        public string ImagesFolder => Path.Combine(ModsRoot, "cards", "images");
        /// <summary>The game's own HD folder (mods/cards/hd), always read: Import Cards' High Resolution Card Images.</summary>
        public string HdFolder => Path.Combine(ModsRoot, "cards", "hd");
        /// <summary>mods/cards, where the card back (back.png or back.jpg) goes.</summary>
        public string CardsFolder => Path.Combine(ModsRoot, "cards");
        /// <summary>Folder names compare without case on Windows (E:\Mods and e:\mods are one folder), exactly elsewhere.</summary>
        private static StringComparison PathCase =>
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        public string ChoicesFile => Path.Combine(ModsRoot, "cards", "art-styles.txt");

        /// <summary>A card's art style changed (its id), or 0 when the preferred style changed (every card).</summary>
        public event Action<uint>? Changed;

        private Texture2D? _back;
        private bool _backLoaded;

        /// <summary>
        /// The card back: mods/cards/back.png (or .jpg), else an EDOPro install's textures/cover.png; null draws the
        /// plain brown back.
        /// </summary>
        public Texture2D? Back
        {
            get
            {
                if (_backLoaded) return _back;
                _backLoaded = true;
                var candidates = new List<string> { Path.Combine(ModsRoot, "cards", "back.png"), Path.Combine(ModsRoot, "cards", "back.jpg") };
                if (_edopro != null) candidates.Add(Path.Combine(Path.GetDirectoryName(_edopro.TrimEnd('/'))!, "textures", "cover.png"));
                foreach (var path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "card back" };
                    if (texture.LoadImage(File.ReadAllBytes(path))) return _back = texture;
                    UnityEngine.Object.Destroy(texture);
                }
                return null;
            }
        }
        public string ModsRoot { get; }

        /// <summary>
        /// <paramref name="picsOverride"/>: the EDOPro pictures folder (-pics); <paramref name="hdOverride"/>: the HD picture
        /// folders instead of the settings' (each -hdpics flag's value; "off" for none).
        /// </summary>
        public CardArt(ICardDataProvider cards, string modsRoot, string? picsOverride, IReadOnlyList<string>? hdOverride = null)
        {
            _cards = cards;
            ModsRoot = modsRoot;
            var own = ImagesFolder;
            Directory.CreateDirectory(own); // so players can find where to put pictures
            _folders.Add(own);

            var settings = Path.Combine(modsRoot, "settings.txt");
            var edopro = picsOverride ?? ReadSetting(settings, "edopro_pics") ?? FindEdoproPics();
            if (!string.IsNullOrEmpty(edopro) && Directory.Exists(edopro)) _folders.Add(_edopro = edopro!);
            bool hdOff = hdOverride != null && hdOverride.Any(h => h.Trim().Equals("off", StringComparison.OrdinalIgnoreCase));
            var hd = hdOverride == null ? ReadSettings(settings, "hd_pics")
                : hdOff ? new List<string>()
                : new List<string>(hdOverride);
            foreach (var folder in hd)
            {
                if (folder.Trim().Length == 0) continue; // an empty hd_pics: line
                var path = Normal(Expand(folder.Trim()));
                if (!Directory.Exists(path)) Debug.LogWarning($"HD pictures folder not found: {path}");
                else if (!_folders.Any(f => string.Equals(Normal(f), path, StringComparison.Ordinal)))
                {
                    _folders.Add(path);
                    _hd.Add(path);
                }
            }
            // The game's own HD folder (Import Cards fills it), read as an hd_pics: line would be: not twice when a line
            // names it already (the same Normal() comparison), and not with -hdpics off.
            if (!hdOff)
            {
                try { Directory.CreateDirectory(HdFolder); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Debug.LogWarning($"Couldn't make {HdFolder}: {e.Message}"); }
                var ownHd = Normal(HdFolder);
                if (Directory.Exists(ownHd) && !_folders.Any(f => string.Equals(Normal(f), ownHd, PathCase)))
                {
                    _folders.Add(ownHd);
                    _hd.Add(ownHd);
                }
            }
            // Kept in ~/Library/Caches on a Mac (the temporary folder is emptied every few days).
            ArtLoader.CacheFolder = Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor
                ? Path.Combine(Home, "Library", "Caches", Application.identifier, "card-art")
                : Path.Combine(Application.persistentDataPath, "card-art-cache");
            ArtLoader.TrimCacheSoon();
            ScanStyles();
            LoadChoices();
        }

        /// <summary>The EDOPro pictures folder in use (null: none), and the HD folders, as given to a new lookup (style check).</summary>
        public string? EdoproFolder => _edopro;
        public IReadOnlyList<string> HdFolders => _hd;
        /// <summary>Whether the game's own HD folder is read on this run (not with -hdpics off).</summary>
        public bool ReadsHdFolder => _hd.Any(f => string.Equals(Normal(f), Normal(HdFolder), PathCase));
        private readonly List<string> _hd = new List<string>();

        /// <summary>A folder's full path without a trailing separator, so "pics" and "pics/" are one folder.</summary>
        private static string Normal(string path)
        {
            try { path = Path.GetFullPath(path); }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { }
            return path.Length > 1 ? path.TrimEnd('/', '\\') : path;
        }

        /// <summary>The style folders, by name (Standard not included).</summary>
        public IReadOnlyList<string> Styles => new List<string>(_styles.Keys);

        /// <summary>A style's name as shown: "Standard", or the folder's name with a capital.</summary>
        public static string Label(string style) => style.Length == 0 ? "Standard" : char.ToUpperInvariant(style[0]) + style.Substring(1);

        /// <summary>Reads the style folders again (pictures added while the game runs).</summary>
        public void ScanStyles()
        {
            _styles.Clear();
            if (Directory.Exists(StylesRoot))
                foreach (var dir in Directory.GetDirectories(StylesRoot))
                {
                    var name = Path.GetFileName(dir);
                    if (name.StartsWith(".") || name.Equals("standard", StringComparison.OrdinalIgnoreCase)) continue;
                    var codes = new HashSet<uint>();
                    foreach (var file in Directory.GetFiles(dir))
                        if (Array.IndexOf(Extensions, Path.GetExtension(file).ToLowerInvariant()) >= 0
                            && uint.TryParse(Path.GetFileNameWithoutExtension(file), out var code)) codes.Add(code);
                    _styles[name] = codes;
                }
            _keys.Clear();
            ForgetAll();
        }

        /// <summary>
        /// The id a card's styles and style choice go by: its own, or its base card's when it has no Standard picture of
        /// its own (original-text versions); an alternate artwork with its own picture keeps its own id.
        /// </summary>
        public uint ArtKey(uint code)
        {
            if (_keys.TryGetValue(code, out var known)) return known;
            var alias = _cards.Get(code)?.Alias ?? 0;
            bool own = alias == 0 || FindFile(_folders, code) != null;
            foreach (var codes in _styles.Values) own |= codes.Contains(code);
            return _keys[code] = own ? code : alias;
        }
        private readonly Dictionary<uint, uint> _keys = new Dictionary<uint, uint>();

        private bool HasStyle(string style, uint key) => style.Length > 0 && _styles.TryGetValue(style, out var codes) && codes.Contains(key);

        /// <summary>The styles with a picture of a card: Standard first, then each style folder that has one.</summary>
        public IReadOnlyList<string> StylesFor(uint code)
        {
            var key = ArtKey(code);
            var list = new List<string> { Standard };
            foreach (var pair in _styles)
                if (pair.Value.Contains(key)) list.Add(pair.Key);
            return list;
        }

        /// <summary>The style a card is drawn in: its own choice, else the preferred style, else Standard.</summary>
        public string StyleOf(uint code)
        {
            var key = ArtKey(code);
            if (_choices.TryGetValue(key, out var chosen) && (chosen.Length == 0 || HasStyle(chosen, key))) return chosen;
            return Fallback(key);
        }

        private string Fallback(uint key) => HasStyle(_preferred, key) ? _preferred : Standard;

        /// <summary>The style chosen for this card itself, or null when it follows the preferred style.</summary>
        public string? ChoiceFor(uint code) => _choices.TryGetValue(ArtKey(code), out var chosen) ? chosen : null;

        /// <summary>
        /// Draws a card in a style (saved). Choosing what it would get anyway (the preferred style, or Standard) clears its
        /// own choice, so it follows the preference again.
        /// </summary>
        public void Choose(uint code, string style)
        {
            var key = ArtKey(code);
            if (style == Fallback(key)) _choices.Remove(key);
            else _choices[key] = style;
            SaveChoices();
            Forget(key);
            Changed?.Invoke(code);
        }

        /// <summary>The Artwork button: the card's next style that has a picture of it. Returns the new style.</summary>
        public string CycleStyle(uint code, int step = 1)
        {
            var options = StylesFor(code);
            int at = Math.Max(0, IndexOf(options, StyleOf(code)));
            var next = options[((at + step) % options.Count + options.Count) % options.Count];
            Choose(code, next);
            return next;
        }

        private static int IndexOf(IReadOnlyList<string> list, string style)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], style, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The style used for cards with no choice of their own (Standard: none). Saved.</summary>
        public string Preferred
        {
            get => _preferred;
            set
            {
                _preferred = value;
                SaveChoices();
                ForgetAll();
                Changed?.Invoke(0);
            }
        }

        /// <summary>Settings: the next preferred style (Standard, then each style folder).</summary>
        public void CyclePreferred()
        {
            var options = new List<string> { Standard };
            options.AddRange(_styles.Keys);
            Preferred = options[(Math.Max(0, IndexOf(options, _preferred)) + 1) % options.Count];
        }

        // art-styles.txt: "prefer: <style>" and "<card id>: <style>" lines ("standard" for the Standard pictures).
        private void LoadChoices()
        {
            _choices.Clear();
            _preferred = Standard;
            if (!File.Exists(ChoicesFile)) return;
            foreach (var raw in File.ReadAllLines(ChoicesFile))
            {
                var line = raw.Trim();
                var colon = line.IndexOf(':');
                if (line.StartsWith("#") || colon <= 0) continue;
                var key = line.Substring(0, colon).Trim();
                var value = line.Substring(colon + 1).Trim();
                var style = value.Equals("standard", StringComparison.OrdinalIgnoreCase) ? Standard : value;
                if (key.Equals("prefer", StringComparison.OrdinalIgnoreCase)) _preferred = style;
                else if (uint.TryParse(key, out var code)) _choices[code] = style;
            }
        }

        private void SaveChoices()
        {
            var lines = new List<string>
            {
                "# Card picture styles (folders in cards/styles/), chosen in the game. \"prefer\" is for cards with no choice of their own.",
                $"prefer: {(_preferred.Length == 0 ? "standard" : _preferred)}",
            };
            var codes = new List<uint>(_choices.Keys);
            codes.Sort();
            foreach (var code in codes) lines.Add($"{code}: {(_choices[code].Length == 0 ? "standard" : _choices[code])}");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ChoicesFile)!);
                File.WriteAllLines(ChoicesFile, lines);
            }
            catch (Exception e) { Debug.LogWarning($"Couldn't save art styles to {ChoicesFile}: {e.Message}"); }
        }

        /// <summary>Drops loaded pictures of a card (any printing that goes by that art key), so they load in the new style.</summary>
        private void Forget(uint key)
        {
            foreach (var code in new List<uint>(_cache.Keys))
                if (code == key || ArtKey(code) == key) Drop(code);
            for (var node = _full.First; node != null;)
            {
                var next = node.Next;
                if (node.Value.code == key || ArtKey(node.Value.code) == key) DropFull(node);
                node = next;
            }
            FreeRetired();
        }

        private void ForgetAll()
        {
            foreach (var code in new List<uint>(_cache.Keys)) Drop(code);
            while (_full.First != null) DropFull(_full.First);
            _files.Clear(); // pictures may have been added
            FreeRetired();
        }

        private void Drop(uint code)
        {
            // Not destroyed: a card on screen may still show it until it is drawn again.
            _cache.Remove(code);
            _recent.Remove(code);
        }

        /// <summary>Whole pictures no longer kept: destroyed once no view shows them (see <see cref="FreeRetired"/>).</summary>
        private readonly List<Texture2D> _retired = new List<Texture2D>();

        private void DropFull(LinkedListNode<(uint code, Texture2D? texture)> node)
        {
            _full.Remove(node);
            if (node.Value.texture != null) _retired.Add(node.Value.texture);
        }

        /// <summary>
        /// Destroys the retired whole pictures no view shows any more (a reader, a full-size slab: at most a couple at once),
        /// at about 5 MB each; one still showing waits for the next time. Runs only when a whole picture is dropped, which
        /// is rare, so looking through the views then is cheap enough.
        /// </summary>
        private void FreeRetired()
        {
            if (_retired.Count == 0) return;
            var shown = new HashSet<Texture>();
            foreach (var raw in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (raw.texture != null) shown.Add(raw.texture);
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                if (_retired[i] != null && shown.Contains(_retired[i])) continue;
                if (_retired[i] != null) UnityEngine.Object.Destroy(_retired[i]);
                _retired.RemoveAt(i);
            }
        }

        /// <summary>
        /// The picture for a card, or null if none is installed: at most <see cref="ArtLoader.Cap"/> pixels tall, mipmapped
        /// (and compressed when big), for everyday views. Loads on first use and keeps recent ones.
        /// </summary>
        public Texture2D? Get(uint code)
        {
            if (code == 0) return null;
            if (_cache.TryGetValue(code, out var cached))
            {
                Touch(code);
                return cached;
            }
            Texture2D? texture = null;
            foreach (var (path, name) in Files(code))
                if ((texture = ArtLoader.Load(path, name)) != null) break;
            _cache[code] = texture;
            Touch(code);
            Evict();
            return texture;
        }

        /// <summary>
        /// The whole picture for the big views (the card readers, a full-size slab), where a capped one would look soft;
        /// the everyday one when it isn't bigger than the cap. Keeps the last few in memory, and every one on disk
        /// (<see cref="ArtLoader.LoadFull"/>), so reopening a reader doesn't decode again.
        /// </summary>
        public Texture2D? GetFull(uint code)
        {
            if (code == 0) return null;
            for (var node = _full.First; node != null; node = node.Next)
                if (node.Value.code == code)
                {
                    _full.Remove(node);
                    _full.AddFirst(node);
                    return node.Value.texture ?? Get(code);
                }
            Texture2D? texture = null;
            foreach (var (path, name) in Files(code))
            {
                if (ArtLoader.ImageSize(path).height <= ArtLoader.Cap) break; // the everyday picture is the whole one
                if ((texture = ArtLoader.LoadFull(path, name)) != null) break;
            }
            _full.AddFirst((code, texture));
            if (_full.Count > MaxFull)
            {
                while (_full.Count > MaxFull) DropFull(_full.Last!);
                FreeRetired();
            }
            return texture ?? Get(code);
        }

        /// <summary>
        /// The files a card's picture can come from, best first, with the texture's name: its art style's picture, or its
        /// Standard pictures (biggest first), then its base card's for an alternate artwork (they usually share it).
        /// </summary>
        private IEnumerable<(string path, string name)> Files(uint code)
        {
            var style = StyleOf(code);
            if (style.Length > 0)
            {
                var key = ArtKey(code);
                if (FindFile(new[] { Path.Combine(StylesRoot, style) }, key) is { } styled) yield return (styled, $"art {key} {style}");
            }
            else
                foreach (var path in StandardFiles(code)) yield return (path, $"art {code}");
            var alias = _cards.Get(code)?.Alias ?? 0;
            if (alias != 0)
                foreach (var path in StandardFiles(alias)) yield return (path, $"art {alias}");
        }

        /// <summary>A card's Standard picture in each folder that has one, the biggest first (folder order breaks ties).</summary>
        private string[] StandardFiles(uint code)
        {
            if (_files.TryGetValue(code, out var known)) return known;
            var found = new List<string>();
            foreach (var folder in _folders)
                if (FindFile(new[] { folder }, code) is { } path) found.Add(path);
            if (found.Count > 1)
            {
                var areas = found.ConvertAll(p => { var (w, h) = ArtLoader.ImageSize(p); return (long)w * h; });
                var order = new List<int>();
                for (int i = 0; i < found.Count; i++) order.Add(i);
                order.Sort((a, b) => areas[b] != areas[a] ? areas[b].CompareTo(areas[a]) : a.CompareTo(b));
                found = order.ConvertAll(i => found[i]);
            }
            return _files[code] = found.ToArray();
        }

        private List<uint>? _installed;

        /// <summary>Ids of cards with a picture installed (read from the folders once).</summary>
        public IReadOnlyList<uint> Installed
        {
            get
            {
                if (_installed != null) return _installed;
                var set = new HashSet<uint>();
                foreach (var folder in _folders)
                    foreach (var ext in Extensions)
                        foreach (var file in Directory.GetFiles(folder, "*" + ext))
                            if (uint.TryParse(Path.GetFileNameWithoutExtension(file), out var code)) set.Add(code);
                return _installed = new List<uint>(set);
            }
        }

        /// <summary>Card pictures in one folder ("&lt;card id&gt;.jpg / .png / .jpeg", a card counted once): Import Cards' counts.</summary>
        public int CountIn(string folder)
        {
            var codes = new HashSet<uint>();
            try
            {
                if (!Directory.Exists(folder)) return 0;
                foreach (var file in Directory.EnumerateFiles(folder))
                    if (Array.IndexOf(Extensions, Path.GetExtension(file).ToLowerInvariant()) >= 0
                        && uint.TryParse(Path.GetFileNameWithoutExtension(file), out var code)) codes.Add(code);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            return codes.Count;
        }

        /// <summary>
        /// Pictures changed on disk (Import Cards): forgets the file lookups, the installed list, art keys, loaded pictures and
        /// the card back, reads the style folders again, and raises <see cref="Changed"/>(0) so open views redraw. The disk
        /// cache needs nothing: an entry whose source file's length or write time changed is made again (ArtLoader.ReadCache),
        /// and one for a deleted file is never read and goes when the cache trims. Textures a view may still show aren't
        /// destroyed here (see <see cref="Drop"/> and <see cref="FreeRetired"/>).
        /// </summary>
        public void Refresh()
        {
            _installed = null;
            // The old back may still be on screen (a pile, a preview): retired, destroyed once nothing shows it.
            if (_back != null) _retired.Add(_back);
            _back = null;
            _backLoaded = false;
            ScanStyles(); // also forgets art keys, loaded pictures and file lookups (ForgetAll)
            Changed?.Invoke(0);
        }

        /// <summary>How many cards have a Standard picture (a card with pictures in several folders counts once).</summary>
        public int Available => Installed.Count;

        private static string? FindFile(IEnumerable<string> folders, uint code)
        {
            foreach (var folder in folders)
                foreach (var ext in Extensions)
                {
                    var path = Path.Combine(folder, code + ext);
                    if (File.Exists(path)) return path;
                }
            return null;
        }

        private void Touch(uint code)
        {
            _recent.Remove(code);
            _recent.AddFirst(code);
        }

        private void Evict()
        {
            while (_recent.Count > MaxCached)
            {
                var oldest = _recent.Last!.Value;
                _recent.RemoveLast();
                // Not destroyed here: a card on screen may still be showing it (it would go blank). Dropped pictures
                // are freed once nothing shows them.
                _cache.Remove(oldest);
                if (++_dropped % 150 == 0) Resources.UnloadUnusedAssets();
            }
        }
        private int _dropped;

        private static string? ReadSetting(string file, string key)
        {
            var values = ReadSettings(file, key);
            return values.Count > 0 ? values[0] : null;
        }

        /// <summary>Every "key: value" line's value for a key, in order ("~" for your home folder).</summary>
        private static List<string> ReadSettings(string file, string key)
        {
            var values = new List<string>();
            if (!File.Exists(file)) return values;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                if (colon > 0 && line.Substring(0, colon).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    values.Add(Expand(line.Substring(colon + 1).Trim()));
            }
            return values;
        }

        private static string Expand(string path) => path.StartsWith("~") ? Path.Combine(Home, path.Substring(1).TrimStart('/')) : path;

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>Common places an EDOPro (Project Ignis) install keeps its downloaded pictures.</summary>
        private static string? FindEdoproPics()
        {
            foreach (var candidate in new[]
                     {
                         Path.Combine(Home, "ProjectIgnis", "pics"),
                         Path.Combine(Home, "Applications", "ProjectIgnis", "pics"),
                         Path.Combine(Home, "Downloads", "ProjectIgnis", "pics"),
                         Path.Combine(Home, "Games", "ProjectIgnis", "pics"),
                         "/Applications/ProjectIgnis/pics",
                     })
            {
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }
    }
}
