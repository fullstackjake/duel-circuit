using System;
using System.Collections.Generic;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The slab's generated pictures, made once and kept (like <see cref="CardWear"/>'s atlas): soft and crisp rounded
    /// rects for the case's shadow, side and wells (9-sliced), the gauge's flat-top hexagon, the rim's ring and band for
    /// grid tiles, and the hologram sticker (four masks the glass shader colours with the light). Drawn at 2 pixels per
    /// layout unit, so edges stay crisp on a Retina screen.
    /// </summary>
    public static class SlabTextures
    {
        private const float Density = 2; // texture pixels per layout unit
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

        /// <summary>
        /// A rounded rect (corner <paramref name="radius"/>) filled with <paramref name="fill"/>, its edge feathered over
        /// <paramref name="blur"/> each way (0: anti-aliased only), with an optional inner <paramref name="outline"/> of
        /// <paramref name="outlineWidth"/>. 9-sliced; the image must be the shape's size plus <paramref name="blur"/> each side.
        /// </summary>
        public static Sprite RoundRect(float radius, float blur, Color fill, Color outline = default, float outlineWidth = 0)
        {
            var key = FormattableString.Invariant($"rr {radius:0.##} {blur:0.##} {fill} {outline} {outlineWidth:0.##}");
            if (Sprites.TryGetValue(key, out var known)) return known;
            int border = Mathf.CeilToInt((2 * blur + radius + 1) * Density);
            int size = 2 * border + 4;
            float pad = blur * Density, r = radius * Density, b = blur * Density, ow = outlineWidth * Density;
            var px = new Color32[size * size];
            float half = (size - 2 * pad) / 2;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - size / 2f, y + 0.5f - size / 2f);
                    float d = RoundRectDistance(p, new Vector2(half, half), r);
                    float a = b > 0 ? 1 - Smooth(-b, b, d) : Mathf.Clamp01(0.5f - d);
                    var c = fill;
                    if (ow > 0) c = Color.Lerp(fill, outline, Mathf.Clamp01(ow + 0.5f + d)); // inside the outer ow pixels
                    px[y * size + x] = new Color(c.r, c.g, c.b, c.a * a);
                }
            var sprite = MakeSprite(px, size, size, new Vector4(border, border, border, border), mips: false);
            Sprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A white rect whose bottom corners are rounded (corner <paramref name="radius"/>), the top ones square: a strip along
        /// a card's bottom edge that follows its rounded corners. 9-sliced.
        /// </summary>
        public static Sprite RoundBottom(float radius)
        {
            radius = Mathf.Max(0.5f, Mathf.Round(radius * 2) / 2);
            var key = FormattableString.Invariant($"rb {radius:0.#}");
            if (Sprites.TryGetValue(key, out var known)) return known;
            int border = Mathf.CeilToInt(radius * Density) + 1;
            int size = 2 * border + 2;
            float r = radius * Density;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance into the bottom corners' quarter circles; elsewhere fully inside.
                    float cx = x + 0.5f < r ? r - (x + 0.5f) : x + 0.5f > size - r ? x + 0.5f - (size - r) : 0;
                    float cy = y + 0.5f < r ? r - (y + 0.5f) : 0;
                    float d = cx > 0 && cy > 0 ? Mathf.Sqrt(cx * cx + cy * cy) - r : -1;
                    px[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
                }
            return Sprites[key] = MakeSprite(px, size, size, new Vector4(border, border, border, border), mips: false);
        }

        /// <summary>A flat-top hexagon (white, anti-aliased) for the gauge, 116x100 pixels (the Full gauge at 2x).</summary>
        public static Sprite Hexagon
        {
            get
            {
                if (Sprites.TryGetValue("hex", out var known)) return known;
                const int w = 116, h = 100;
                var px = new Color32[w * h];
                // Flat top: vertices left and right; the apothem is half the height, the circumradius half the width.
                float apothem = h / 2f - 0.5f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = Mathf.Abs(x + 0.5f - w / 2f), dy = Mathf.Abs(y + 0.5f - h / 2f);
                        float d = Mathf.Max(dy, 0.866f * dx + 0.5f * dy) - apothem;
                        px[y * w + x] = new Color(1, 1, 1, Mathf.Clamp01(0.5f - d));
                    }
                return Sprites["hex"] = MakeSprite(px, w, h, Vector4.zero, mips: true);
            }
        }

        /// <summary>
        /// The grid rim's glass (9-sliced, <paramref name="ring"/> units wide, then a 1-unit gilt line at the well edge):
        /// a dark outer line, a pale veil, a crest lit on the top and left (the idle light's side) and the house's gilt.
        /// </summary>
        public static Sprite RimRing(int ring)
        {
            var key = "ring " + ring;
            if (Sprites.TryGetValue(key, out var known)) return known;
            const float radius = 4;
            int border = Mathf.CeilToInt((ring + 1 + radius) * Density) + 1;
            int size = 2 * border + 4;
            var px = new Color32[size * size];
            var gilt = Ui.Hex("#B08A3E");
            var key2 = new Vector2(-0.6f, 1).normalized;
            float half = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                    float d = RoundRectDistance(p, new Vector2(half, half), radius * Density) / Density; // units, negative inside
                    float inside = -d;
                    Color c = new Color(0, 0, 0, 0);
                    if (inside < 0) { px[y * size + x] = c; continue; }
                    float edge = Mathf.Clamp01(inside * Density + 0.5f); // anti-aliased outer edge
                    var n = Normal(p, new Vector2(half, half), radius * Density);
                    float lit = Mathf.Clamp01(Vector2.Dot(n, key2));
                    if (inside < 1) c = new Color(0, 0, 0, 0.55f);
                    else if (inside < ring)
                    {
                        // Veil, with the crest 1.5 units in, bright where the light falls.
                        float crest = Mathf.Clamp01(1 - Mathf.Abs(inside - 1.5f) * 1.6f) * (0.25f + 0.6f * lit);
                        c = Color.Lerp(new Color(0.80f, 0.85f, 0.92f, 0.18f), new Color(0.97f, 0.98f, 1f, 0.9f), crest);
                    }
                    else if (inside < ring + 1) c = gilt;
                    c.a *= edge;
                    px[y * size + x] = c;
                }
            var sprite = MakeSprite(px, size, size, new Vector4(border, border, border, border), mips: false);
            return Sprites[key] = sprite;
        }

        /// <summary>
        /// The grid rim's reflection: one faint diagonal band (white, at most 5%) where the idle light puts the slab's
        /// glare, never over the top 13% of the card (its name). Stretched over the card.
        /// </summary>
        public static Sprite RimBand
        {
            get
            {
                if (Sprites.TryGetValue("band", out var known)) return known;
                const int w = 88, h = 128;
                var px = new Color32[w * h];
                var nb = new Vector2(1, -0.7f).normalized;
                // The idle light (0.30, 0.78) on an 88x128 card puts band A's centre here (see SlabGlass.shader).
                var lp = new Vector2((CardFoil.IdleLight.x - 0.5f) * w, (CardFoil.IdleLight.y - 0.5f) * h);
                float centre = 0.55f * Vector2.Dot(lp, nb), width = 0.11f * w;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (y > h * 0.87f) continue;
                        var p = new Vector2(x + 0.5f - w / 2f, y + 0.5f - h / 2f);
                        float q = Mathf.Abs(Vector2.Dot(p, nb) - centre);
                        float a = 0.05f * (1 - Smooth(0.4f * width, width, q));
                        px[y * w + x] = new Color(1, 1, 1, a);
                    }
                return Sprites["band"] = MakeSprite(px, w, h, Vector4.zero, mips: true);
            }
        }

        private static Texture2D? _sticker;

        /// <summary>
        /// The hologram sticker's masks, 112x128, a pointy-top hexagon filling it: R the engraved dial (concentric hexagons
        /// every 4 texels crossed by 24 spokes, between 30% and 78% of the radius, cleared round the letters), G the "G&amp;G"
    /// monogram (5-texel strokes), B the border
        /// (3 texels) and an inner ring at 82%, A the hexagon. Mipmapped, so it stays clean at 34x39.
        /// </summary>
        public static Texture2D Sticker
        {
            get
            {
                if (_sticker != null) return _sticker;
                const int w = 112, h = 128;
                const float apothem = 55.4f; // circumradius 64 (half the height) x cos 30°
                var monogram = Monogram(0.56f * w);
                var px = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var p = new Vector2(x + 0.5f - w / 2f, y + 0.5f - h / 2f);
                        float hex = Mathf.Max(Mathf.Abs(p.x), 0.5f * Mathf.Abs(p.x) + 0.866f * Mathf.Abs(p.y)); // 0 centre, apothem at the edge
                        float a = Mathf.Clamp01(apothem + 0.5f - hex);
                        float border = Mathf.Clamp01(3.5f - (apothem - hex)) * a;
                        float ring = Mathf.Clamp01(1 - Mathf.Abs(hex - 0.82f * apothem));
                        float lines = 0;
                        if (hex > 0.3f * apothem && hex < 0.78f * apothem)
                        {
                            float rings = Mathf.Clamp01(1 - Mathf.Abs(hex - 4 * Mathf.Round(hex / 4)));
                            float angle = Mathf.Atan2(p.y, p.x), step = Mathf.PI / 12;
                            float spoke = Mathf.Clamp01(1 - Mathf.Abs(p.magnitude * Mathf.Sin(angle - step * Mathf.Round(angle / step))));
                            lines = Mathf.Max(rings, spoke);
                        }
                        // 5-texel strokes (about 1.5 screen pixels at 34x39), with the dial's lines cleared round them so
                        // the letters stand clear.
                        float dm = SegmentsDistance(p, monogram);
                        float g = Mathf.Clamp01(3 - dm);
                        lines *= Mathf.Clamp01((dm - 4) / 2);
                        px[y * w + x] = new Color(lines, g, Mathf.Max(border, ring), a);
                    }
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, true, true)
                {
                    name = "Slab sticker", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                };
                texture.SetPixels32(px);
                texture.Apply(true, true);
                return _sticker = texture;
            }
        }

        /// <summary>
        /// The monogram "G&amp;G" as line segments (texels from the sticker's centre), <paramref name="width"/> texels wide.
        /// Glyphs are polylines on a 10x14 grid, one unit apart; the ampersand is drawn at 0.8x.
        /// </summary>
        private static List<(Vector2 a, Vector2 b)> Monogram(float width)
        {
            var g = new List<Vector2>();
            for (int i = 0; i <= 28; i++)
            {
                float t = Mathf.Deg2Rad * Mathf.Lerp(40, 320, i / 28f);
                g.Add(new Vector2(5 + 4.5f * Mathf.Cos(t), 7 + 6 * Mathf.Sin(t)));
            }
            g.Add(new Vector2(8.45f, 6.2f));
            g.Add(new Vector2(5.6f, 6.2f));
            var amp = new[] { (9.2f, 0f), (2.8f, 8.6f), (2.8f, 11.2f), (4.6f, 13.2f), (6.6f, 11.6f), (6.6f, 9.8f), (1.6f, 5.0f), (1.6f, 2.0f), (3.6f, 0.2f), (6.2f, 0.4f), (9.4f, 4.2f) };
            const float ampWidth = 8f; // 10 units at 0.8x
            float total = 10 + 1 + ampWidth + 1 + 10, scale = width / total;
            var origin = new Vector2(-total / 2, -7);
            var segments = new List<(Vector2, Vector2)>();
            void Add(IList<Vector2> line, Vector2 offset)
            {
                for (int i = 1; i < line.Count; i++)
                    segments.Add(((line[i - 1] + offset + origin) * scale, (line[i] + offset + origin) * scale));
            }
            Add(g, Vector2.zero);
            var ampLine = new List<Vector2>();
            foreach (var (x, y) in amp) ampLine.Add(new Vector2(x, y) * 0.8f);
            Add(ampLine, new Vector2(11, 0));
            Add(g, new Vector2(11 + ampWidth + 1, 0));
            return segments;
        }

        private static float SegmentsDistance(Vector2 p, List<(Vector2 a, Vector2 b)> segments)
        {
            float best = float.MaxValue;
            foreach (var (a, b) in segments)
            {
                var ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
            }
            return best;
        }

        /// <summary>Signed distance to a rounded rect of half size <paramref name="half"/> centred on 0 (negative inside).</summary>
        private static float RoundRectDistance(Vector2 p, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + radius, Mathf.Abs(p.y) - half.y + radius);
            return new Vector2(Mathf.Max(q.x, 0), Mathf.Max(q.y, 0)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
        }

        /// <summary>The outward normal of a rounded rect's nearest edge.</summary>
        private static Vector2 Normal(Vector2 p, Vector2 half, float radius)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + radius, Mathf.Abs(p.y) - half.y + radius);
            var s = new Vector2(Mathf.Sign(p.x), Mathf.Sign(p.y));
            if (q.x > 0 && q.y > 0) return Vector2.Scale(q.normalized, s);
            return q.x > q.y ? new Vector2(s.x, 0) : new Vector2(0, s.y);
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3 - 2 * t);
        }

        private static Sprite MakeSprite(Color32[] px, int w, int h, Vector4 border, bool mips)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, mips)
            {
                name = "Slab sprite", wrapMode = TextureWrapMode.Clamp, filterMode = mips ? FilterMode.Trilinear : FilterMode.Bilinear,
            };
            texture.SetPixels32(px);
            texture.Apply(mips, true);
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100 * Density, 0, SpriteMeshType.FullRect, border);
        }
    }
}
