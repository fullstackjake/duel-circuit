using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The duel mat behind the cards: each player's half has its own look. By default it is painted in code from the
    /// deck's main Attribute (dark violet for DARK, embers for FIRE, deep sea for WATER...): soft clouds, a faint magic
    /// circle and drifting motes of light. The rival and circuit bosses have signature mats (campaign/mats.txt): their
    /// own colors and a motif (flames, waves, gears...). A picture in mods/mats/&lt;character id&gt;.png (or
    /// &lt;deck&gt;.png, or art/mats/ shipped with the game) replaces the painted mat.
    /// While a Field Spell is face-up on a side, its artwork fades in over that half.
    /// </summary>
    public sealed class Playmat : MonoBehaviour
    {
        /// <summary>Colors of one mat: deep, mid and accent (zone frames, motes).</summary>
        public readonly struct Theme
        {
            public readonly Color Deep, Mid, Accent;
            public Theme(string deep, string mid, string accent) { Deep = Ui.Hex(deep); Mid = Ui.Hex(mid); Accent = Ui.Hex(accent); }
        }

        /// <summary>A character's signature mat: colors and a motif painted over the clouds.</summary>
        public readonly struct Signature
        {
            public readonly Theme Theme;
            public readonly string Motif;
            public Signature(Theme theme, string motif) { Theme = theme; Motif = motif; }
        }

        public static readonly string[] Motifs =
            { "rays", "flames", "waves", "gears", "circuit", "runes", "sparkles", "chains", "scales", "stripes", "staff", "ring", "void", "mist", "sigil",
              "eclipse", "halos", "arrows", "brick" };

        /// <summary>Signature mats by character id, read from campaign/mats.txt.</summary>
        public static readonly Dictionary<string, Signature> Signatures = new Dictionary<string, Signature>();

        /// <summary>Reads campaign/mats.txt: "&lt;id&gt;: &lt;deep&gt; &lt;mid&gt; &lt;accent&gt; &lt;motif&gt;" lines.</summary>
        public static void LoadSignatures(string file)
        {
            Signatures.Clear();
            if (!File.Exists(file)) return;
            foreach (var raw in File.ReadAllLines(file))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var colon = line.IndexOf(':');
                var parts = line.Substring(colon + 1).Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (colon <= 0 || parts.Length < 4 || System.Array.IndexOf(Motifs, parts[3]) < 0)
                {
                    Debug.LogWarning($"mats.txt: can't read \"{line}\"");
                    continue;
                }
                Signatures[line.Substring(0, colon).Trim()] = new Signature(new Theme(parts[0], parts[1], parts[2]), parts[3]);
            }
        }

        private static readonly List<string> PictureFolders = new List<string>();

        /// <summary>Where mat pictures are looked for: mods/mats (yours first), the game's StreamingAssets/mats, art/mats.</summary>
        public static void InitFolders(string modsRoot)
        {
            PictureFolders.Clear();
            PictureFolders.Add(Path.Combine(modsRoot, "mats"));
            PictureFolders.Add(Path.Combine(Application.streamingAssetsPath, "mats"));
            PictureFolders.Add(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "art", "mats")));
        }

        private static readonly Dictionary<uint, Theme> ByAttribute = new Dictionary<uint, Theme>
        {
            [0x01] = new Theme("#1B1710", "#4A3B22", "#C9A15A"), // EARTH: sandstone
            [0x02] = new Theme("#081726", "#14466E", "#5FC8FF"), // WATER: deep sea
            [0x04] = new Theme("#1E0A08", "#6B1E12", "#FF8A3D"), // FIRE: embers
            [0x08] = new Theme("#0B1A14", "#1F5A3C", "#7BE0A8"), // WIND: forest
            [0x10] = new Theme("#1C1A12", "#5E5326", "#FFE38A"), // LIGHT: gold
            [0x20] = new Theme("#110A1C", "#3E1E66", "#B98CFF"), // DARK: violet
            [0x40] = new Theme("#1B1408", "#6B4E12", "#FFD45E"), // DIVINE
        };
        private static readonly Theme Neutral = new Theme("#0E1320", "#22324F", "#7FA6E0");

        /// <summary>
        /// The default mats, the space look: a blue starfield across the table and a golden glow on your side (seat 0),
        /// with the zone frames in the same blue and gold. A picture mat or a signature mat replaces them.
        /// </summary>
        public static readonly Theme SpaceYou = new Theme("#120B04", "#6E4A12", "#FFC24A");
        public static readonly Theme SpaceThem = new Theme("#02060F", "#12397E", "#5FB4FF");
        public static Theme Space(int seat) => seat == 0 ? SpaceYou : SpaceThem;

        private readonly RawImage[] _base = new RawImage[2], _field = new RawImage[2];
        private readonly Theme[] _themes = { Neutral, Neutral };
        private readonly uint[] _fieldCode = new uint[2];
        private readonly float[] _fieldAlpha = new float[2], _fieldTarget = new float[2];
        private readonly List<(RawImage img, Vector2 from, Vector2 velocity, float phase, int side)> _motes = new List<(RawImage, Vector2, Vector2, float, int)>();
        private readonly List<Graphic> _frames = new List<Graphic>();
        private readonly List<(Graphic g, int side)> _sideFrames = new List<(Graphic, int)>();
        private RectTransform _root = null!;
        private float _width = 1380, _height = 1030;

        public static Playmat Create(RectTransform field)
        {
            var root = Ui.Stretched("Playmat", field, Vector2.zero, Vector2.one);
            var mat = root.gameObject.AddComponent<Playmat>();
            mat._root = root;
            for (int p = 0; p < 2; p++)
            {
                var half = Ui.Stretched($"Half{p}", root, new Vector2(0, p == 0 ? 0 : 0.5f), new Vector2(1, p == 0 ? 0.5f : 1));
                mat._base[p] = half.gameObject.AddComponent<RawImage>();
                mat._base[p].raycastTarget = false;
                var art = Ui.Stretched("FieldSpell", half, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                art.raycastTarget = false;
                art.color = new Color(1, 1, 1, 0);
                mat._field[p] = art;
            }
            // The line between the two fields.
            var seam = Ui.Stretched("Seam", root, new Vector2(0, 0.5f), new Vector2(1, 0.5f));
            seam.sizeDelta = new Vector2(0, 3);
            mat._frames.Add(Ui.Image(seam, new Color(1f, 0.8f, 0.36f, 0.45f)));
            mat.SetTheme(0, SpaceYou, null, "space");
            mat.SetTheme(1, SpaceThem, null, "space");
            // Motes of light drifting over each half.
            var rng = new System.Random(5);
            for (int i = 0; i < 26; i++)
            {
                int side = i % 2;
                var rt = Ui.Placed("Mote", root, Vector2.zero, Vector2.one * (6 + rng.Next(10)));
                var img = rt.gameObject.AddComponent<RawImage>();
                img.texture = BoardFx.Textures.Glow;
                img.raycastTarget = false;
                var from = new Vector2((float)rng.NextDouble() * 1300 - 650, (float)rng.NextDouble() * 480 * (side == 0 ? -1 : 1));
                var velocity = new Vector2((float)rng.NextDouble() * 16 - 8, 6 + (float)rng.NextDouble() * 14);
                mat._motes.Add((img, from, velocity, (float)rng.NextDouble() * 10, side));
            }
            return mat;
        }

        /// <summary>The theme for a deck: its main monsters' most common Attribute.</summary>
        public static Theme ThemeFor(Deck deck, ICardDataProvider cards)
        {
            var attribute = deck.Main.Select(cards.Get).Where(c => c != null && (c.Type & CardType.Monster) != 0)
                .GroupBy(c => c!.Attribute).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            return ByAttribute.TryGetValue(attribute, out var theme) ? theme : Neutral;
        }

        /// <summary>
        /// Sets one side's mat: <paramref name="picture"/> (a custom mat) when given, else painted from the theme.
        /// Zone frames on that side take the theme's accent.
        /// </summary>
        /// <summary>For checks: the theme a side's mat was last set to.</summary>
        public Theme ThemeOf(int side) => _themes[side];

        public void SetTheme(int side, Theme theme, Texture2D? picture, string motif = "")
        {
            _themes[side] = theme;
            _base[side].texture = picture ?? Painted(theme, side, motif);
            _base[side].color = picture != null ? new Color(0.75f, 0.75f, 0.75f, 1) : Color.white;
            // A picture is drawn as its owner sees it: the opponent's is turned around, like their cards.
            _base[side].uvRect = picture != null && side == 1 ? new Rect(1, 1, -1, -1) : new Rect(0, 0, 1, 1);
            foreach (var (g, s) in _sideFrames)
                if (s == side) g.color = new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, g.color.a);
            foreach (var m in _motes.Where(m => m.side == side)) m.img.color = new Color(theme.Accent.r, theme.Accent.g, theme.Accent.b, 0);
        }

        /// <summary>A mat picture: &lt;name&gt;.png (or .jpg) in the mat folders, the first name that has one.</summary>
        public static Texture2D? LoadCustom(params string[] names)
        {
            foreach (var name in names.Where(n => !string.IsNullOrEmpty(n)))
                foreach (var folder in PictureFolders)
                foreach (var ext in new[] { ".png", ".jpg" })
                {
                    var path = Path.Combine(folder, name + ext);
                    if (!File.Exists(path)) continue;
                    var tex = new Texture2D(2, 2);
                    if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
                }
            return null;
        }

        /// <summary>Registers a zone frame to tint with a side's accent.</summary>
        public void AddFrame(Graphic frame, int side)
        {
            _sideFrames.Add((frame, side));
            var t = _themes[side].Accent;
            frame.color = new Color(t.r, t.g, t.b, frame.color.a);
        }

        /// <summary>The face-up Field Spell on each side (0 for none); its art fades in over that half.</summary>
        public void SetFieldSpells(uint[] codes)
        {
            for (int p = 0; p < 2; p++)
            {
                if (codes[p] == _fieldCode[p]) continue;
                _fieldCode[p] = codes[p];
                var art = codes[p] == 0 ? null : CardView.Art?.Get(codes[p]);
                if (art != null)
                {
                    _field[p].texture = art;
                    // The artwork box of a card picture (roughly its middle), stretched over the half.
                    _field[p].uvRect = new Rect(0.13f, 0.32f, 0.74f, 0.42f);
                    _fieldTarget[p] = 0.62f;
                }
                else _fieldTarget[p] = 0;
            }
        }

        private void Update()
        {
            var t = Time.unscaledTime;
            for (int p = 0; p < 2; p++)
            {
                if (Mathf.Approximately(_fieldAlpha[p], _fieldTarget[p])) continue;
                _fieldAlpha[p] = Mathf.MoveTowards(_fieldAlpha[p], _fieldTarget[p], Time.unscaledDeltaTime * 1.2f);
                _field[p].color = new Color(0.85f, 0.85f, 0.85f, _fieldAlpha[p]); // a touch darker, so cards on top stay readable
            }
            foreach (var (img, from, velocity, phase, side) in _motes)
            {
                if (!FxSettings.Particles) { img.enabled = false; continue; }
                img.enabled = true;
                // Each mote drifts up through its half and wraps around, pulsing softly.
                float travel = (t * velocity.y + phase * 60) % 480;
                float y = side == 0 ? -480 + travel : travel;
                var x = from.x + Mathf.Sin(t * 0.3f + phase) * 40 + velocity.x * Mathf.Sin(t * 0.1f + phase);
                img.rectTransform.anchoredPosition = new Vector2(x, y);
                var a = _themes[side].Accent;
                float fade = Mathf.Sin(travel / 480f * Mathf.PI);
                img.color = new Color(a.r, a.g, a.b, 0.5f * fade * (0.6f + 0.4f * Mathf.Sin(t * 2 + phase)));
            }
        }

        private static readonly Dictionary<(Theme, int, string), Texture2D> Painted_ = new Dictionary<(Theme, int, string), Texture2D>();

        /// <summary>
        /// A mat painted in code: soft clouds over a gradient, a faint magic circle (or a signature motif), darker toward
        /// the edges. Readable, so checks can save it.
        /// </summary>
        public static Texture2D Painted(Theme theme, int side, string motif = "")
        {
            if (Painted_.TryGetValue((theme, side, motif), out var cached)) return cached;
            if (motif == "space") return Painted_[(theme, side, motif)] = PaintedSpace(theme, side);
            const int w = 512, h = 192;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            float seed = side * 37.3f + theme.Mid.r * 11;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w, v = (float)y / h;
                    // Toward the middle of the board (the seam) the mat glows a little.
                    float towardSeam = side == 0 ? v : 1 - v;
                    float clouds = Mathf.PerlinNoise(u * 4 + seed, v * 2.2f + seed) * 0.6f + Mathf.PerlinNoise(u * 11 + seed, v * 6 + seed) * 0.4f;
                    var c = Color.Lerp(theme.Deep, theme.Mid, Mathf.Clamp01(clouds * 0.9f * (0.45f + 0.55f * towardSeam)));
                    // A faint magic circle in the middle of the half.
                    float dx = (u - 0.5f) * (w / (float)h), dy = v - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Max(0, 1 - Mathf.Abs(r - 0.36f) * 60) + Mathf.Max(0, 1 - Mathf.Abs(r - 0.42f) * 90) * 0.7f;
                    float spokes = Mathf.Max(0, Mathf.Cos(Mathf.Atan2(dy, dx) * 12)) * Mathf.Max(0, 1 - Mathf.Abs(r - 0.39f) * 30);
                    if (motif.Length == 0) c = Color.Lerp(c, theme.Accent, 0.1f * Mathf.Clamp01(ring + spokes * 0.5f));
                    // The motif starts at the seam side for side 0; flip v so both halves mirror around the seam.
                    else c = Color.Lerp(c, theme.Accent, 0.24f * Mathf.Clamp01(MotifAt(motif, u, side == 0 ? 1 - v : v, seed)));
                    // Darker at the left and right edges.
                    float edge = Mathf.Clamp01(Mathf.Min(u, 1 - u) * 6);
                    c *= 0.65f + 0.35f * edge;
                    c.a = 1;
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            Painted_[(theme, side, motif)] = tex;
            return tex;
        }

        /// <summary>
        /// The space mat: a nebula over a deep gradient (brightest toward the seam), a field of stars and a few four-pointed
        /// twinkles, darker at the edges.
        /// </summary>
        private static Texture2D PaintedSpace(Theme theme, int side)
        {
            const int w = 1024, h = 384;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            float seed = side * 53.1f + 7;
            // A second hue for the nebula: violet in the blue, ember orange in the gold.
            var tint = side == 0 ? new Color(1f, 0.45f, 0.15f) : new Color(0.55f, 0.35f, 1f);
            var twinkles = new List<(float x, float y, float size)>();
            var rng = new System.Random(side == 0 ? 11 : 23);
            for (int i = 0; i < 9; i++) twinkles.Add(((float)rng.NextDouble(), (float)rng.NextDouble(), 0.006f + (float)rng.NextDouble() * 0.014f));
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w, v = (float)y / h;
                    float towardSeam = side == 0 ? v : 1 - v;
                    float n1 = Mathf.PerlinNoise(u * 3 + seed, v * 1.6f + seed);
                    float n2 = Mathf.PerlinNoise(u * 9 - seed, v * 4.5f + seed * 0.5f);
                    float nebula = Mathf.Clamp01(n1 * 0.7f + n2 * 0.45f - 0.25f);
                    var c = Color.Lerp(theme.Deep, theme.Mid, Mathf.Clamp01(nebula * (0.5f + 0.7f * towardSeam)));
                    c = Color.Lerp(c, tint * theme.Mid.maxColorComponent, Mathf.Clamp01((n2 - 0.55f) * 1.6f) * 0.35f);
                    // A glow along the seam.
                    c = Color.Lerp(c, theme.Accent, Mathf.Pow(towardSeam, 6) * 0.22f);
                    // Stars: rare bright pixels, a little bigger now and then.
                    float star = Hash(x, y + seed);
                    if (star > 0.9965f) c = Color.Lerp(c, Color.Lerp(Color.white, theme.Accent, 0.3f), (star - 0.9965f) / 0.0035f * 0.9f);
                    // Twinkles: a soft four-pointed light.
                    foreach (var (tx, ty, size) in twinkles)
                    {
                        float dx = Mathf.Abs(u - tx) * w / h, dy = Mathf.Abs(v - ty);
                        float cross = Mathf.Exp(-dx / size * 0.15f) * Mathf.Exp(-dy / size * 6) + Mathf.Exp(-dy / size * 0.15f) * Mathf.Exp(-dx / size * 6);
                        float core = Mathf.Exp(-(dx * dx + dy * dy) / (size * size * 4));
                        c = Color.Lerp(c, Color.Lerp(Color.white, theme.Accent, 0.4f), Mathf.Clamp01(cross * 0.35f + core * 0.8f));
                    }
                    float edge = Mathf.Clamp01(Mathf.Min(u, 1 - u) * 5);
                    c *= 0.6f + 0.4f * edge;
                    c.a = 1;
                    pixels[y * w + x] = c;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static float Hash(float x, float y) { var n = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f; return n - Mathf.Floor(n); }
        private static float Line(float distance, float width) => Mathf.Clamp01(1 - Mathf.Abs(distance) / width);

        /// <summary>
        /// How strongly a motif covers a point (0..1). u runs left to right; s is the distance from the seam (0 at the
        /// middle of the board, 1 at the player's edge). x/y are in half-heights, centered.
        /// </summary>
        private static float MotifAt(string motif, float u, float s, float seed)
        {
            const float aspect = 512f / 192f;
            float x = (u - 0.5f) * aspect, y = s - 0.5f;
            float r = Mathf.Sqrt(x * x + y * y), angle = Mathf.Atan2(y, x);
            switch (motif)
            {
                case "rays":
                {
                    // A sunburst rising from the seam, like a banner over a charge.
                    float a = Mathf.Atan2(x, s + 0.05f);
                    float ray = Mathf.Pow(Mathf.Max(0, Mathf.Cos(a * 22)), 10);
                    return ray * Mathf.Clamp01(1.3f - s) + Line(Mathf.Sqrt(x * x + s * s) - 0.55f, 0.03f) * 0.8f;
                }
                case "flames":
                {
                    // Tongues of fire climbing from the outer edge.
                    float d = 1 - s;
                    // Each tongue sways sideways as it rises and narrows to a point.
                    float sway = u * 14 + Mathf.Sin(d * 7 + u * 20) * 0.35f;
                    float tongue = Mathf.Abs(Mathf.Sin(sway * Mathf.PI * 0.5f));
                    float height = 0.45f + 0.4f * Mathf.PerlinNoise(Mathf.Floor(sway) * 1.7f + seed, 1);
                    float body = Mathf.Clamp01((tongue - d / height) * 2.2f);
                    return body + Mathf.Clamp01(1 - d * 2.5f) * 0.55f;
                }
                case "waves":
                {
                    float wave = s * 7 + Mathf.Sin(u * 16 + s * 3) * 0.35f + Mathf.Sin(u * 5 + seed) * 0.4f;
                    return Line(wave - Mathf.Round(wave), 0.07f) * (0.5f + 0.5f * s);
                }
                case "gears":
                {
                    float best = 0;
                    for (int i = 0; i < 5; i++)
                    {
                        float cx = (Hash(i, seed) - 0.5f) * aspect * 0.9f, cy = Hash(i + 9, seed) - 0.5f, size = 0.16f + Hash(i + 3, seed) * 0.2f;
                        float dx = x - cx, dy = y - cy, dr = Mathf.Sqrt(dx * dx + dy * dy), da = Mathf.Atan2(dy, dx);
                        int teeth = 8 + (int)(Hash(i + 5, seed) * 6);
                        float edge = size * (1 + 0.14f * Mathf.Sign(Mathf.Sin(da * teeth + i)));
                        best = Mathf.Max(best, Line(dr - edge, 0.02f), Line(dr - size * 0.45f, 0.015f) * 0.8f);
                    }
                    return best;
                }
                case "circuit":
                {
                    float cell = 0.12f;
                    float gx = Mathf.Floor(x / cell), gy = Mathf.Floor(y / cell);
                    float fx = x / cell - gx - 0.5f, fy = y / cell - gy - 0.5f;
                    float h1 = Hash(gx, gy + seed), h2 = Hash(gx + 31, gy + seed);
                    float trace = 0;
                    if (h1 > 0.55f) trace = Mathf.Max(trace, Line(fy, 0.08f));
                    if (h2 > 0.6f) trace = Mathf.Max(trace, Line(fx, 0.08f));
                    if (h1 > 0.55f && h2 > 0.6f) trace = Mathf.Max(trace, Mathf.Clamp01(1 - Mathf.Sqrt(fx * fx + fy * fy) / 0.22f) * 1.4f);
                    return trace * 0.9f;
                }
                case "runes":
                {
                    // Two rings with blocks of glyphs between them.
                    float rings = Line(r - 0.34f, 0.012f) + Line(r - 0.44f, 0.012f) + Line(r - 0.2f, 0.01f) * 0.6f;
                    float segment = Mathf.Floor((angle + Mathf.PI) / (2 * Mathf.PI) * 36);
                    float glyph = r > 0.36f && r < 0.42f && Hash(segment, seed) > 0.4f
                        ? Mathf.Clamp01(Mathf.Abs(Mathf.Sin((angle + Mathf.PI) * 36 * 2 + Hash(segment, 1) * 6)) * 1.5f) * 0.8f : 0;
                    return rings + glyph;
                }
                case "sparkles":
                {
                    float cell = 0.16f, best = 0;
                    float gx = Mathf.Floor(x / cell), gy = Mathf.Floor(y / cell);
                    if (Hash(gx, gy + seed) > 0.55f)
                    {
                        float px = (gx + 0.2f + Hash(gx + 7, gy) * 0.6f) * cell, py = (gy + 0.2f + Hash(gx, gy + 7) * 0.6f) * cell;
                        float dx = Mathf.Abs(x - px), dy = Mathf.Abs(y - py), size = 0.02f + Hash(gx + 3, gy + 3) * 0.04f;
                        // A four-pointed star.
                        best = Mathf.Clamp01(1 - (dx * dy) / (size * 0.012f)) * Mathf.Clamp01(1 - (dx + dy) / (size * 2.2f)) * 1.5f;
                    }
                    return best;
                }
                case "chains":
                {
                    // Two chains crossing the half diagonally; links alternate face-on and edge-on.
                    float best = 0;
                    for (int c = 0; c < 2; c++)
                    {
                        float slope = c == 0 ? 0.32f : -0.32f;
                        float along = (x + slope * y) / Mathf.Sqrt(1 + slope * slope), across = (y - slope * x) / Mathf.Sqrt(1 + slope * slope);
                        float link = 0.13f, k = Mathf.Round(along / link), lx = along - k * link;
                        bool face = ((int)k & 1) == 0;
                        float ex = lx / (link * 0.62f), ey = across / (face ? 0.045f : 0.012f);
                        best = Mathf.Max(best, Line(Mathf.Sqrt(ex * ex + ey * ey) - 1, 0.28f));
                    }
                    return best;
                }
                case "brick":
                {
                    // The alley's wall: running bond (each course offset half a brick), mortar lines, and a wet sheen
                    // that catches the neon toward the seam.
                    float course = 0.1f, brick = 0.27f;
                    float row = Mathf.Floor(y / course), offset = ((int)row & 1) == 0 ? 0 : brick * 0.5f;
                    float by = y - (row + 0.5f) * course, bx = x + offset - (Mathf.Floor((x + offset) / brick) + 0.5f) * brick;
                    float mortar = Mathf.Max(Line(Mathf.Abs(by) - course * 0.5f, 0.012f), Line(Mathf.Abs(bx) - brick * 0.5f, 0.012f));
                    float face = (1 - mortar) * (0.12f + 0.12f * Hash(Mathf.Floor((x + offset) / brick), row + seed));
                    float sheen = Mathf.Pow(Mathf.Clamp01(1 - s), 3) * 0.45f * (0.6f + 0.4f * Mathf.PerlinNoise(u * 30 + seed, s * 3));
                    return mortar * 0.85f + face + sheen * (1 - mortar);
                }
                case "scales":
                {
                    float size = 0.11f;
                    float row = Mathf.Floor(y / (size * 0.6f));
                    float offset = ((int)row & 1) == 0 ? 0 : size * 0.5f;
                    float col = Mathf.Floor((x + offset) / size);
                    float cx = col * size - offset + size * 0.5f, cy = row * size * 0.6f;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    return Line(d - size * 0.55f, 0.012f) * (0.4f + 0.6f * s);
                }
                case "stripes":
                {
                    // Hazard stripes along the outer edge and chevrons pointing at the seam.
                    float band = s > 0.82f ? Mathf.Clamp01(Mathf.Sign(Mathf.Sin((x + s) * 30)) + 0.2f) : 0;
                    float chevron = s * 3 - Mathf.Abs(x) * 0.8f;
                    float chev = Mathf.Abs(x) < 0.5f && s < 0.7f ? Line(chevron - Mathf.Round(chevron), 0.09f) : 0;
                    return band * 0.9f + chev;
                }
                case "staff":
                {
                    // Five music lines curving across, with notes on them.
                    float bend = Mathf.Sin(u * 7 + seed) * 0.08f;
                    float lines = 0;
                    for (int i = 0; i < 5; i++) lines = Mathf.Max(lines, Line(y - bend - (i - 2) * 0.06f, 0.008f));
                    float cell = 0.22f, k = Mathf.Floor(x / cell);
                    float nx = (k + 0.5f) * cell, ny = bend + (Mathf.Floor(Hash(k, seed) * 9) - 4) * 0.03f;
                    float ex = (x - nx) / 0.034f, ey = (y - ny) / 0.024f;
                    float note = Hash(k + 4, seed) > 0.3f ? Mathf.Clamp01(1.2f - Mathf.Sqrt(ex * ex + ey * ey)) * 2 : 0;
                    float stem = Hash(k + 4, seed) > 0.3f && Mathf.Abs(x - nx - 0.032f) < 0.006f && y > ny && y < ny + 0.16f ? 1 : 0;
                    return Mathf.Max(lines, note, stem);
                }
                case "ring":
                {
                    // A wrestling ring: three ropes around a rounded square, corner posts.
                    float best = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        float half = 0.36f + i * 0.05f;
                        float qx = Mathf.Max(Mathf.Abs(x) / 1.9f - half + 0.08f, 0), qy = Mathf.Max(Mathf.Abs(y) - half + 0.08f, 0);
                        float box = Mathf.Sqrt(qx * qx + qy * qy) + Mathf.Min(Mathf.Max(Mathf.Abs(x) / 1.9f - half + 0.08f, Mathf.Abs(y) - half + 0.08f), 0) - 0.08f;
                        best = Mathf.Max(best, Line(box, 0.01f));
                    }
                    return best;
                }
                case "void":
                {
                    // Cracks spreading from an empty center.
                    float crack = 0;
                    for (int i = 0; i < 9; i++)
                    {
                        float a = Hash(i, seed) * Mathf.PI * 2, len = 0.4f + Hash(i + 2, seed) * 0.9f;
                        float jag = Mathf.Sin(r * 40 + i) * 0.015f + Mathf.Sin(r * 13 + i * 2) * 0.03f;
                        float across = -Mathf.Sin(a) * x + Mathf.Cos(a) * y + jag, along = Mathf.Cos(a) * x + Mathf.Sin(a) * y;
                        if (along > 0 && along < len) crack = Mathf.Max(crack, Line(across, 0.01f * (1 - along / len) + 0.003f));
                    }
                    return crack + Mathf.Clamp01(1 - r / 0.18f) * 0.5f;
                }
                case "mist":
                {
                    float wisp = Mathf.PerlinNoise(u * 3 + seed, s * 2) * 0.6f + Mathf.PerlinNoise(u * 9 - seed, s * 5 + 2) * 0.4f;
                    return Mathf.Clamp01((wisp - 0.45f) * 4) * (0.4f + 0.6f * s);
                }
                case "sigil":
                {
                    // A circle holding two interlocked triangles, under a crown of short marks.
                    float circle = Line(r - 0.4f, 0.012f) + Line(r - 0.45f, 0.008f) * 0.7f;
                    float tri = 0;
                    for (int t = 0; t < 2; t++)
                    {
                        float tilt = t * Mathf.PI / 3;
                        float edge = -1;
                        for (int e = 0; e < 3; e++)
                        {
                            float a = tilt + e * Mathf.PI * 2 / 3 + Mathf.PI / 2;
                            edge = Mathf.Max(edge, x * Mathf.Cos(a) + y * Mathf.Sin(a));
                        }
                        tri = Mathf.Max(tri, Line(edge - 0.2f, 0.01f));
                    }
                    float marks = r > 0.47f && r < 0.52f ? Mathf.Clamp01(Mathf.Cos(angle * 24) * 4 - 3) : 0;
                    return circle + tri + marks;
                }
                case "eclipse":
                {
                    // Light meeting dark: an empty disc with a bright rim, its corona streaming out, one bead of light
                    // on the rim and a faint outer halo.
                    const float disc = 0.27f;
                    float streamers = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(angle * 5 + seed) * Mathf.Cos(angle * 3 - seed * 0.5f));
                    float corona = r > disc ? Mathf.Exp(-(r - disc) / 0.07f) * streamers : 0;
                    float bx = x + 0.74f * disc, by = y - 0.68f * disc;
                    float bead = Mathf.Exp(-(bx * bx + by * by) / 0.0016f);
                    return Line(r - disc, 0.012f) + corona * 0.65f + bead + Line(r - 0.46f, 0.006f) * 0.35f;
                }
                case "halos":
                {
                    // A row of rings seen edge-on (small in the middle, larger toward the sides) with stars streaming
                    // through them along the line between the two rows of zones.
                    float rings = 0;
                    for (int k = -3; k <= 3; k++)
                    {
                        float hb = 0.12f + 0.1f * Mathf.Abs(k), ha = hb * 0.3f;
                        float ex = x - k * 0.4f, ey = y;
                        float q = Mathf.Sqrt(ex * ex / (ha * ha) + ey * ey / (hb * hb));
                        // Distance to the ellipse, roughly: how far q is from 1 over how fast q changes here.
                        float slope = Mathf.Sqrt(ex * ex / (ha * ha * ha * ha) + ey * ey / (hb * hb * hb * hb)) / Mathf.Max(q, 0.001f);
                        rings = Mathf.Max(rings, Line((q - 1) / Mathf.Max(slope, 0.001f), 0.012f) * (0.45f + 0.55f * Mathf.Abs(k) / 3f));
                    }
                    float step = 0.2f, n = Mathf.Round(x / step), sx = x - n * step;
                    float star = Mathf.Exp(-(sx * sx + y * y) / 0.0008f) * (0.4f + 0.6f * Hash(n, seed));
                    return Mathf.Max(rings, star) + Line(y, 0.006f) * 0.2f;
                }
                case "arrows":
                {
                    // A square marker with eight arrowheads pointing out (to its sides and corners), and trails of
                    // chevrons running from it toward the left and right edges.
                    float qx = Mathf.Abs(x) - 0.24f, qy = Mathf.Abs(y) - 0.24f;
                    float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
                    float box = Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0);
                    float marks = Line(box, 0.012f);
                    // Faint links from the square out to the trails.
                    if (Mathf.Abs(x) > 0.3f && Mathf.Abs(x) < 0.62f) marks = Mathf.Max(marks, Line(y, 0.005f) * 0.35f);
                    for (int i = 0; i < 8; i++)
                    {
                        float dirX = Mathf.Cos(i * Mathf.PI / 4), dirY = Mathf.Sin(i * Mathf.PI / 4);
                        // The tip; the base sits on the square's side or corner.
                        float reach = (i & 1) == 0 ? 0.3f : 0.4f;
                        float px = x - dirX * reach, py = y - dirY * reach;
                        float along = px * dirX + py * dirY, across = Mathf.Abs(py * dirX - px * dirY);
                        float head = Mathf.Clamp01(-(along + across) / 0.012f) * Mathf.Clamp01((along + 0.06f) / 0.012f);
                        marks = Mathf.Max(marks, head);
                    }
                    float trail = Mathf.Abs(x) - 0.62f;
                    if (trail > 0 && trail < 0.6f)
                    {
                        float period = 0.15f, k = Mathf.Floor(trail / period), local = trail - (k + 0.5f) * period;
                        float chevron = Mathf.Abs(y) < 0.07f ? Line(local - 0.035f + Mathf.Abs(y), 0.014f) : 0;
                        marks = Mathf.Max(marks, chevron * (1 - k * 0.2f));
                    }
                    return marks;
                }
            }
            return 0;
        }
    }
}
