using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Character portraits: &lt;character id&gt;.png from mods/portraits (your own, first), the game's StreamingAssets/portraits,
    /// or art/portraits in the repo (editor). Loaded once each; a character without one simply shows none.
    /// </summary>
    public static class Portraits
    {
        private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>();
        private static readonly List<string> Folders = new List<string>();

        /// <summary>The nameless alley regulars' faces: art/portraits/alley-grunt-NN.png (the owner's art, 2026-11-05).</summary>
        public const string GruntPrefix = "alley-grunt-";
        private static List<string>? _grunts;

        /// <summary>Every grunt portrait id found in the portrait folders, sorted (so a handle's face doesn't move between runs).</summary>
        public static IReadOnlyList<string> Grunts
        {
            get
            {
                if (_grunts != null) return _grunts;
                var ids = new SortedSet<string>(System.StringComparer.Ordinal);
                foreach (var folder in Folders)
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in Directory.GetFiles(folder, GruntPrefix + "*.png")) ids.Add(Path.GetFileNameWithoutExtension(file));
                }
                return _grunts = new List<string>(ids);
            }
        }

        /// <summary>
        /// The face of a nameless alley regular, picked by its handle (a stable FNV-1a hash, never string.GetHashCode), so the
        /// same regular looks the same on the table panel, the bracket, the versus and the board. Null when there's no art.
        /// </summary>
        public static string? GruntFor(string handle)
        {
            var grunts = Grunts;
            if (grunts.Count == 0 || string.IsNullOrEmpty(handle)) return null;
            uint hash = 2166136261;
            foreach (char c in handle.ToLowerInvariant()) { hash ^= c; hash *= 16777619; }
            return grunts[(int)(hash % (uint)grunts.Count)];
        }

        public static void Init(string modsRoot)
        {
            _grunts = null;
            Folders.Clear();
            Folders.Add(Path.Combine(modsRoot, "portraits"));
            Folders.Add(Path.Combine(Application.streamingAssetsPath, "portraits"));
            Folders.Add(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "portraits")));
        }

        public static Texture2D? Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (Cache.TryGetValue(id, out var cached)) return cached;
            Texture2D? texture = null;
            foreach (var folder in Folders)
            {
                var path = Path.Combine(folder, id + ".png");
                if (!File.Exists(path)) continue;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                if (!texture.LoadImage(File.ReadAllBytes(path))) { texture = null; continue; }
                break;
            }
            Cache[id] = texture;
            return texture;
        }

        public static bool Has(string id) => Get(id) != null;

        /// <summary>
        /// A head-and-shoulders crop: the portrait fills <paramref name="box"/>'s width and hangs from its top edge (the
        /// box should mask the rest). False when they have no portrait.
        /// </summary>
        public static bool PlaceHead(RectTransform box, string id, bool mirror = false)
        {
            var texture = Get(id);
            if (texture == null) return false;
            var size = box.rect.size == Vector2.zero ? box.sizeDelta : box.rect.size;
            float scale = size.x * 1.15f / texture.width;
            var rt = Ui.Rect("Portrait", box);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, 4);
            rt.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
            if (mirror) rt.localScale = new Vector3(-1, 1, 1);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            return true;
        }

        /// <summary>
        /// Draws a character's portrait inside <paramref name="box"/>, as large as fits without stretching, standing on the
        /// box's bottom edge. Returns false (and draws nothing) when they have no portrait.
        /// </summary>
        public static bool Place(RectTransform box, string id, bool mirror = false)
        {
            var texture = Get(id);
            if (texture == null) return false;
            var size = box.rect.size == Vector2.zero ? box.sizeDelta : box.rect.size;
            float scale = Mathf.Min(size.x / texture.width, size.y / texture.height);
            var rt = Ui.Rect("Portrait", box);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
            if (mirror) rt.localScale = new Vector3(-1, 1, 1);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            return true;
        }
    }
}
