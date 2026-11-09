using System.IO;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The game's logo: logo.png from the game's StreamingAssets/logo (copied there by the build from art/logo) or art/logo
    /// in the repo (editor). Loaded once. Without it the title and menu draw the name as text (<see cref="GameInfo.Title"/>).
    /// </summary>
    public static class GameLogo
    {
        private static bool _loaded;
        private static Texture2D? _texture;

        public static Texture2D? Texture
        {
            get
            {
                if (_loaded) return _texture;
                _loaded = true;
                foreach (var folder in new[]
                         {
                             Path.Combine(Application.streamingAssetsPath, "logo"),
                             Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "logo")),
                         })
                {
                    var path = Path.Combine(folder, "logo.png");
                    if (!File.Exists(path)) continue;
                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(path); }
                    catch (IOException) { continue; }                     // locked or unreadable: the text title stands in
                    catch (System.UnauthorizedAccessException) { continue; }
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
                    if (!texture.LoadImage(bytes)) continue;
                    texture.filterMode = FilterMode.Trilinear;
                    _texture = texture;
                    break;
                }
                return _texture;
            }
        }

        /// <summary>The logo's width for a given height (its own aspect; 1.5 if none).</summary>
        public static float WidthFor(float height) => Texture is Texture2D t && t.height > 0 ? height * t.width / t.height : height * 1.5f;
    }
}
