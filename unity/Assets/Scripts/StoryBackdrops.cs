using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The story screen's backdrops, one per place a scene plays in (<see cref="StoryPlaces"/>): the owner's
    /// art/backdrops/&lt;place&gt;.png if it's there (the game's StreamingAssets/backdrops, copied by the build, or the repo's
    /// art/backdrops in the editor; fitted to 1920 × 1080 at most), else painted in code on a <see cref="BackdropCanvas"/> in the
    /// place's theme (the main street's navy and lamp gold; the Back Alley's brick, wet asphalt and neon, with
    /// <see cref="AlleyMaterials"/>' brick). Every backdrop is dim and soft (darkest where the name plate, the dialogue box and
    /// the portraits stand) so they draw over it and read; owner art is drawn at <see cref="ArtTint"/> for the same reason.
    ///
    /// Painting a place takes a few hundred milliseconds, so <see cref="Warm"/> paints them all on a worker thread, one after
    /// another (one canvas alive at a time), and <see cref="Pump"/> uploads one finished place a frame on the main thread. A
    /// place asked for before it's ready is painted on the spot. Uploaded textures keep no CPU copy (~2 MB of GPU each); a
    /// small brightness map per place stays for the checks (<see cref="Brightness"/>).
    /// </summary>
    public static partial class StoryBackdrops
    {
        /// <summary>The painted backdrops' size: half the 1920 × 1080 layout, stretched over the screen (soft by design).</summary>
        public const int Width = 960, Height = 540;
        /// <summary>The brightness maps' size (one cell per 4 × 4 canvas pixels, 8 × 8 of the layout's).</summary>
        private const int LumaW = 240, LumaH = 135;
        /// <summary>Owner art is fitted inside this (larger art is scaled down once, at load).</summary>
        public const int ArtMaxW = 1920, ArtMaxH = 1080;

        /// <summary>Owner art's tint (it may be brighter than the painted backdrops; the story keeps it dim).</summary>
        public static readonly Color ArtTint = new Color(0.62f, 0.62f, 0.62f, 1);

        private sealed class Painting
        {
            public Color32[] Pixels = Array.Empty<Color32>();
            public float[] Luma = Array.Empty<float>();
            public long Ms;
        }

        private static readonly Dictionary<string, Texture2D> Painted = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D?> Art = new Dictionary<string, Texture2D?>();
        /// <summary>Each place's brightness map, as drawn (art at its tint).</summary>
        private static readonly Dictionary<string, float[]> LumaOf = new Dictionary<string, float[]>();
        /// <summary>Places the worker has painted, waiting for <see cref="Pump"/>.</summary>
        private static readonly ConcurrentDictionary<string, Painting> Ready = new ConcurrentDictionary<string, Painting>();
        private static Task? _warming;
        private static Color[]? _brick;
        private static int _brickW, _brickH;

        /// <summary>How long each place took to paint (ms, wherever it was painted), for the checks' report.</summary>
        public static readonly ConcurrentDictionary<string, long> PaintMs = new ConcurrentDictionary<string, long>();

        /// <summary>The theme a place draws in: the alley's places the Back Alley's, the rest the main street's.</summary>
        public static UiTheme ThemeOf(string place) => StoryPlaces.IsAlley(place) ? UiTheme.BackAlley : UiTheme.MainStreet;

        /// <summary>The backdrop for a place (an unknown one: the shop floor's), the owner's art first.</summary>
        public static Texture2D Get(string place) => ArtFor(place) ?? PaintedFor(place);

        /// <summary>Whether a place shows the owner's art rather than the painted backdrop.</summary>
        public static bool IsArt(string place) => ArtFor(place) != null;

        /// <summary>Starts painting every place without art on a worker thread (main thread; once).</summary>
        public static void Warm()
        {
            if (_warming != null) return;
            Prepare();
            var places = new List<string>();
            foreach (var place in StoryPlaces.Known)
                if (ArtFor(place) == null && !Painted.ContainsKey(place)) places.Add(place);
            _warming = Task.Run(() =>
            {
                foreach (var place in places)
                {
                    if (Ready.ContainsKey(place)) continue;
                    try { Ready[place] = PaintNow(place); }
                    catch (Exception e) { UnityEngine.Debug.LogWarning($"story backdrop {place}: {e.Message}"); }
                }
            });
        }

        /// <summary>Uploads one place the worker finished (main thread, each frame; a few milliseconds when there's one).</summary>
        public static void Pump()
        {
            if (Ready.IsEmpty) return;
            foreach (var place in Ready.Keys)
            {
                if (!Ready.TryRemove(place, out var painting)) continue;
                if (!Painted.ContainsKey(place)) Upload(place, painting);
                return;
            }
        }

        /// <summary>The painted backdrop (never the art): the worker's if it's done, else painted now.</summary>
        public static Texture2D PaintedFor(string place)
        {
            if (!StoryPlaces.IsKnown(place)) place = StoryPlaces.ShopFloor;
            if (Painted.TryGetValue(place, out var done)) return done;
            if (!Ready.TryRemove(place, out var painting))
            {
                Prepare();
                painting = PaintNow(place);
            }
            return Upload(place, painting);
        }

        /// <summary>For checks: forgets a painted place (its texture freed), so the next show paints it cold.</summary>
        public static void DebugForget(string place)
        {
            if (Painted.TryGetValue(place, out var texture)) UnityEngine.Object.Destroy(texture);
            Painted.Remove(place);
            Ready.TryRemove(place, out _);
            if (!Art.TryGetValue(place, out var art) || art == null) LumaOf.Remove(place);
        }

        /// <summary>
        /// The mean brightness (Rec. 709 luma, 0..1) of a place's backdrop over a box of the 1920 × 1080 layout, as drawn (owner
        /// art at <see cref="ArtTint"/>), from its brightness map.
        /// </summary>
        public static float? Brightness(string place, float x, float y, float w, float h)
        {
            Get(place);
            if (!LumaOf.TryGetValue(StoryPlaces.IsKnown(place) ? place : StoryPlaces.ShopFloor, out var luma)) return null;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x / 1920 * LumaW), 0, LumaW - 1), x1 = Mathf.Clamp(Mathf.CeilToInt((x + w) / 1920 * LumaW), x0 + 1, LumaW);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(y / 1080 * LumaH), 0, LumaH - 1), y1 = Mathf.Clamp(Mathf.CeilToInt((y + h) / 1080 * LumaH), y0 + 1, LumaH);
            double sum = 0;
            for (int cy = y0; cy < y1; cy++)
                for (int cx = x0; cx < x1; cx++) sum += luma[cy * LumaW + cx];
            return (float)(sum / ((x1 - x0) * (y1 - y0)));
        }

        /// <summary>What painting needs from Unity, read on the main thread first: the alley's brick pixels, the themes' colors.</summary>
        private static void Prepare()
        {
            if (_brick != null) return;
            var brick = AlleyMaterials.Brick;
            _brick = brick.GetPixels();
            _brickW = brick.width;
            _brickH = brick.height;
            _ = UiTheme.BackAlley.Base;
            _ = UiTheme.NeonCyan;
        }

        /// <summary>Paints a place (any thread): its pixels, its brightness map, how long it took.</summary>
        private static Painting PaintNow(string place)
        {
            var clock = Stopwatch.StartNew();
            var canvas = Paint(place);
            var painting = new Painting { Pixels = canvas.ToPixels(), Luma = canvas.Luma(LumaW, LumaH), Ms = clock.ElapsedMilliseconds };
            PaintMs[place] = painting.Ms;
            return painting;
        }

        private static Texture2D Upload(string place, Painting painting)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Backdrop " + place,
            };
            texture.SetPixels32(painting.Pixels);
            texture.Apply(false, true); // no CPU copy kept
            LumaOf[place] = painting.Luma;
            return Painted[place] = texture;
        }

        private static Texture2D? ArtFor(string place)
        {
            if (Art.TryGetValue(place, out var cached)) return cached;
            Texture2D? texture = null;
            foreach (var folder in new[]
                     {
                         Path.Combine(Application.streamingAssetsPath, "backdrops"),
                         Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "backdrops")),
                     })
            {
                var path = Path.Combine(folder, place + ".png");
                if (!File.Exists(path)) continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch (IOException) { continue; }                 // locked or unreadable: the painted one stands in
                catch (UnauthorizedAccessException) { continue; }
                var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (!loaded.LoadImage(bytes)) { UnityEngine.Object.Destroy(loaded); continue; }
                texture = FitArt(place, loaded);
                break;
            }
            return Art[place] = texture;
        }

        /// <summary>
        /// Owner art, kept lean: its brightness map read (at <see cref="ArtTint"/>), scaled down inside 1920 × 1080 if it's larger
        /// (a new texture; the big one freed), and its CPU copy dropped.
        /// </summary>
        private static Texture2D FitArt(string place, Texture2D loaded)
        {
            var luma = new float[LumaW * LumaH];
            for (int cy = 0; cy < LumaH; cy++)
                for (int cx = 0; cx < LumaW; cx++)
                {
                    var c = loaded.GetPixelBilinear((cx + 0.5f) / LumaW, 1 - (cy + 0.5f) / LumaH);
                    luma[cy * LumaW + cx] = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * ArtTint.r;
                }
            LumaOf[place] = luma;
            if (loaded.width <= ArtMaxW && loaded.height <= ArtMaxH)
            {
                loaded.Apply(false, true);
                return loaded;
            }
            float scale = Mathf.Min((float)ArtMaxW / loaded.width, (float)ArtMaxH / loaded.height);
            int w = Mathf.Max(1, Mathf.RoundToInt(loaded.width * scale)), h = Mathf.Max(1, Mathf.RoundToInt(loaded.height * scale));
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) pixels[y * w + x] = loaded.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
            var fitted = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Backdrop art " + place };
            fitted.SetPixels32(pixels);
            fitted.Apply(false, true);
            UnityEngine.Object.Destroy(loaded);
            return fitted;
        }

        private static BackdropCanvas Paint(string place)
        {
            switch (place)
            {
                case StoryPlaces.ShopNight: return ShopNight();
                case StoryPlaces.Corkboard: return Corkboard();
                case StoryPlaces.CorkboardTop: return Corkboard(swapped: true);
                case StoryPlaces.BackRoom: return BackRoom();
                case StoryPlaces.Library: return LibraryShelves();
                case StoryPlaces.EveryEra: return EveryEraStage();
                case StoryPlaces.Hall: return Hall();
                case StoryPlaces.Alley: return AlleyWall();
                case StoryPlaces.Basement: return Basement();
                case StoryPlaces.Van: return Van();
                case StoryPlaces.Depot: return Depot();
                default: return ShopFloor();
            }
        }

        // ---- Shared pieces -------------------------------------------------------------------------------------------

        /// <summary>A "#RRGGBB" color, parsed here (painting runs off the main thread, away from Unity's parser).</summary>
        private static Color H(string hex)
        {
            int v = Convert.ToInt32(hex.TrimStart('#'), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1);
        }
        private static Color Dim(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1);

        /// <summary>
        /// A shelf's run of binders standing on a plank at <paramref name="baseY"/>, spines in muted year colors (as the
        /// Library's covers: around the wheel), some with a brass label; a gap of one spine wide at <paramref name="gapAt"/>
        /// (x; -1 for none).
        /// </summary>
        private static void Binders(BackdropCanvas c, float x0, float x1, float baseY, float tall, int seed, float sat, float val, float gapAt = -1)
        {
            var rng = new System.Random(seed);
            float x = x0;
            int k = 0;
            while (x < x1 - 10)
            {
                float w = 13 + rng.Next(0, 9);
                float h = tall * (0.82f + (float)rng.NextDouble() * 0.18f);
                if (gapAt >= 0 && x <= gapAt && x + w > gapAt) { x += w + 3; k++; continue; }
                var cover = Color.HSVToRGB(Mathf.Repeat((seed * 0.13f) + k * 0.083f, 1), sat, val * (0.8f + (float)rng.NextDouble() * 0.3f));
                c.Rect(x, baseY - h, w, h, cover, Dim(cover, 0.7f));
                c.Rect(x, baseY - h, 2, h, Dim(cover, 1.35f), 0.5f);           // the spine's lit edge
                if (rng.Next(3) == 0) c.Rect(x + 3, baseY - h * 0.7f, w - 6, 7, H("#B8862B"), 0.55f); // a brass label
                x += w + 1 + (rng.Next(5) == 0 ? 3 : 0);
                k++;
            }
        }

        /// <summary>A plank: the shelf's top, its lit edge, its shadow under.</summary>
        private static void Plank(BackdropCanvas c, float x0, float x1, float y, Color wood)
        {
            c.Rect(x0, y, x1 - x0, 9, wood, Dim(wood, 0.7f));
            c.Rect(x0, y, x1 - x0, 1.5f, Dim(wood, 1.5f), 0.6f);
            c.Rect(x0, y + 9, x1 - x0, 6, Color.black, 0.35f);
        }

        /// <summary>A lamp hanging on a cord from the ceiling: its shade, the bulb's glow and the light falling under it.</summary>
        private static void Pendant(BackdropCanvas c, float x, float y, Color shade, Color light, float strength, float fall)
        {
            c.Line(x, 0, x, y - 8, 1.5f, H("#05070B"), 0.9f);
            c.Poly(shade, 1, new Vector2(x - 8, y - 9), new Vector2(x + 8, y - 9), new Vector2(x + 22, y + 6), new Vector2(x - 22, y + 6));
            c.Glow(x, y + 7, 60, light, strength * 0.7f);
            c.Rect(x - 14, y + 4, 28, 3, light, 0.8f);
            c.Beam(x, y + 6, 20, y + fall, fall * 0.55f, light, strength * 0.16f);
        }

        /// <summary>Tiny dot-matrix digits and capitals (3 × 5) for boards and signs: each lit dot a square <paramref name="dot"/> wide.</summary>
        private static void Dots(BackdropCanvas c, float x, float y, string text, float dot, Color on, float strength)
        {
            foreach (char ch in text.ToUpperInvariant())
            {
                if (Glyphs.TryGetValue(ch, out var rows))
                    for (int r = 0; r < 5; r++)
                        for (int col = 0; col < 3; col++)
                            if (rows[r][col] == '#')
                            {
                                c.Rect(x + col * dot, y + r * dot, dot * 0.8f, dot * 0.8f, on, 0.9f);
                                c.Glow(x + col * dot + dot * 0.4f, y + r * dot + dot * 0.4f, dot * 1.6f, on, strength * 0.25f);
                            }
                x += dot * 4;
            }
        }

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['0'] = new[] { "###", "#.#", "#.#", "#.#", "###" }, ['1'] = new[] { ".#.", "##.", ".#.", ".#.", "###" },
            ['2'] = new[] { "###", "..#", "###", "#..", "###" }, ['3'] = new[] { "###", "..#", ".##", "..#", "###" },
            ['4'] = new[] { "#.#", "#.#", "###", "..#", "..#" }, ['5'] = new[] { "###", "#..", "###", "..#", "###" },
            ['6'] = new[] { "###", "#..", "###", "#.#", "###" }, ['7'] = new[] { "###", "..#", ".#.", ".#.", ".#." },
            ['8'] = new[] { "###", "#.#", "###", "#.#", "###" }, ['9'] = new[] { "###", "#.#", "###", "..#", "###" },
            ['A'] = new[] { ".#.", "#.#", "###", "#.#", "#.#" }, ['C'] = new[] { "###", "#..", "#..", "#..", "###" },
            ['D'] = new[] { "##.", "#.#", "#.#", "#.#", "##." }, ['E'] = new[] { "###", "#..", "##.", "#..", "###" },
            ['I'] = new[] { "###", ".#.", ".#.", ".#.", "###" }, ['L'] = new[] { "#..", "#..", "#..", "#..", "###" },
            ['M'] = new[] { "#.#", "###", "###", "#.#", "#.#" }, ['N'] = new[] { "##.", "#.#", "#.#", "#.#", "#.#" },
            ['O'] = new[] { "###", "#.#", "#.#", "#.#", "###" }, ['P'] = new[] { "###", "#.#", "###", "#..", "#.." },
            ['R'] = new[] { "##.", "#.#", "##.", "#.#", "#.#" }, ['S'] = new[] { "###", "#..", "###", "..#", "###" },
            ['T'] = new[] { "###", ".#.", ".#.", ".#.", ".#." }, ['U'] = new[] { "#.#", "#.#", "#.#", "#.#", "###" },
            ['W'] = new[] { "#.#", "#.#", "###", "###", "#.#" }, ['Y'] = new[] { "#.#", "#.#", ".#.", ".#.", ".#." },
            ['B'] = new[] { "##.", "#.#", "##.", "#.#", "##." }, ['F'] = new[] { "###", "#..", "##.", "#..", "#.." },
            ['G'] = new[] { "###", "#..", "#.#", "#.#", "###" }, ['H'] = new[] { "#.#", "#.#", "###", "#.#", "#.#" },
            ['K'] = new[] { "#.#", "#.#", "##.", "#.#", "#.#" },
            ['-'] = new[] { "...", "...", "###", "...", "..." }, [' '] = new[] { "...", "...", "...", "...", "..." },
        };
    }
}
