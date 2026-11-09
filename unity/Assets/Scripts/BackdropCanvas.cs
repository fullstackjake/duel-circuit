using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A small raster to paint a story backdrop on (<see cref="StoryBackdrops"/>): colors in a buffer, y down from the top-left
    /// like <see cref="Ui.Box"/>, shapes edged a pixel soft. Paint (normal) blends a color over what's there; Light adds it, for
    /// bulbs, beams and glows. Pure math on plain structs (no Unity calls: its own Perlin noise, textures passed in as pixels),
    /// so it can paint off the main thread; <see cref="ToPixels"/> hands the result over for upload.
    /// </summary>
    public sealed class BackdropCanvas
    {
        public readonly int W, H;
        private readonly Color[] _px;

        public BackdropCanvas(int w, int h, Color fill)
        {
            W = w;
            H = h;
            _px = new Color[w * h];
            for (int i = 0; i < _px.Length; i++) _px[i] = fill;
        }

        /// <summary>The color at a pixel (clamped to the canvas).</summary>
        public Color At(int x, int y) => _px[Mathf.Clamp(y, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)];

        private void Over(int x, int y, Color c, float a)
        {
            if (a <= 0) return;
            int i = y * W + x;
            _px[i] = Color.LerpUnclamped(_px[i], c, Mathf.Min(a, 1));
        }

        private void Add(int x, int y, Color c, float a)
        {
            if (a <= 0) return;
            int i = y * W + x;
            var p = _px[i];
            _px[i] = new Color(Mathf.Min(1, p.r + c.r * a), Mathf.Min(1, p.g + c.g * a), Mathf.Min(1, p.b + c.b * a), 1);
        }

        private void Bounds(float x0, float y0, float x1, float y1, out int ax, out int ay, out int bx, out int by)
        {
            ax = Mathf.Max(0, Mathf.FloorToInt(x0) - 1);
            ay = Mathf.Max(0, Mathf.FloorToInt(y0) - 1);
            bx = Mathf.Min(W - 1, Mathf.CeilToInt(x1) + 1);
            by = Mathf.Min(H - 1, Mathf.CeilToInt(y1) + 1);
        }

        // ---- Fills ---------------------------------------------------------------------------------------------------

        /// <summary>Rows <paramref name="y0"/> to <paramref name="y1"/>, edge to edge, from one color down to another.</summary>
        public void Band(float y0, float y1, Color top, Color bottom)
        {
            for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(H, Mathf.CeilToInt(y1)); y++)
            {
                var c = Color.Lerp(top, bottom, (y - y0) / Mathf.Max(1, y1 - y0));
                for (int x = 0; x < W; x++) _px[y * W + x] = c;
            }
        }

        /// <summary>An axis-aligned box, its color running from <paramref name="top"/> to <paramref name="bottom"/>.</summary>
        public void Rect(float x, float y, float w, float h, Color top, Color bottom, float alpha = 1)
        {
            Bounds(x, y, x + w, y + h, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
            {
                float cy = Mathf.Clamp01(Mathf.Min(py + 1 - y, y + h - py));
                if (cy <= 0) continue;
                var c = Color.Lerp(top, bottom, (py + 0.5f - y) / Mathf.Max(1, h));
                for (int px = ax; px <= bx; px++)
                {
                    float cx = Mathf.Clamp01(Mathf.Min(px + 1 - x, x + w - px));
                    Over(px, py, c, cx * cy * alpha);
                }
            }
        }

        public void Rect(float x, float y, float w, float h, Color c, float alpha = 1) => Rect(x, y, w, h, c, c, alpha);

        /// <summary>A convex polygon (corners in either winding), filled; <paramref name="light"/> adds instead of painting.</summary>
        public void Poly(Color c, float alpha, bool light, params Vector2[] pts)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, area = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                x0 = Mathf.Min(x0, a.x); y0 = Mathf.Min(y0, a.y); x1 = Mathf.Max(x1, a.x); y1 = Mathf.Max(y1, a.y);
                area += a.x * b.y - b.x * a.y;
            }
            float sign = area >= 0 ? 1 : -1;
            Bounds(x0, y0, x1, y1, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    var p = new Vector2(px + 0.5f, py + 0.5f);
                    float d = float.MaxValue;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var a = pts[i];
                        var e = pts[(i + 1) % pts.Length] - a;
                        float len = e.magnitude;
                        if (len < 1e-4f) continue;
                        // Inside is to the left of each edge (for a positive winding): the distance in from that edge.
                        d = Mathf.Min(d, sign * (e.x * (p.y - a.y) - e.y * (p.x - a.x)) / len);
                    }
                    float cover = Mathf.Clamp01(d + 0.5f) * alpha;
                    if (light) Add(px, py, c, cover); else Over(px, py, c, cover);
                }
        }

        public void Poly(Color c, float alpha, params Vector2[] pts) => Poly(c, alpha, false, pts);

        /// <summary>A box turned by <paramref name="degrees"/> about its center (a pinned sheet, a tilted card).</summary>
        public void Turned(float cx, float cy, float w, float h, float degrees, Color c, float alpha = 1)
        {
            float r = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            Vector2 P(float u, float v) => new Vector2(cx + u * cos - v * sin, cy + u * sin + v * cos);
            Poly(c, alpha, P(-w / 2, -h / 2), P(w / 2, -h / 2), P(w / 2, h / 2), P(-w / 2, h / 2));
        }

        /// <summary>A filled disc.</summary>
        public void Disc(float cx, float cy, float r, Color c, float alpha = 1)
        {
            Bounds(cx - r, cy - r, cx + r, cy + r, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    float d = Mathf.Sqrt((px + 0.5f - cx) * (px + 0.5f - cx) + (py + 0.5f - cy) * (py + 0.5f - cy));
                    Over(px, py, c, Mathf.Clamp01(r - d + 0.5f) * alpha);
                }
        }

        /// <summary>A ring (a porthole's rim, a bulb's cage).</summary>
        public void Ring(float cx, float cy, float r, float width, Color c, float alpha = 1)
        {
            Bounds(cx - r - width, cy - r - width, cx + r + width, cy + r + width, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    float d = Mathf.Sqrt((px + 0.5f - cx) * (px + 0.5f - cx) + (py + 0.5f - cy) * (py + 0.5f - cy));
                    Over(px, py, c, Mathf.Clamp01(width / 2 - Mathf.Abs(d - r) + 0.5f) * alpha);
                }
        }

        /// <summary>A straight stroke <paramref name="width"/> wide, round-ended.</summary>
        public void Line(float x0, float y0, float x1, float y1, float width, Color c, float alpha = 1, bool light = false)
        {
            float half = width / 2;
            Bounds(Mathf.Min(x0, x1) - half, Mathf.Min(y0, y1) - half, Mathf.Max(x0, x1) + half, Mathf.Max(y0, y1) + half,
                out int ax, out int ay, out int bx, out int by);
            var a = new Vector2(x0, y0);
            var ab = new Vector2(x1 - x0, y1 - y0);
            float len2 = Mathf.Max(1e-4f, ab.sqrMagnitude);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    var p = new Vector2(px + 0.5f, py + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = (p - (a + ab * t)).magnitude;
                    float cover = Mathf.Clamp01(half - d + 0.5f) * alpha;
                    if (light) Add(px, py, c, cover); else Over(px, py, c, cover);
                }
        }

        // ---- Light ---------------------------------------------------------------------------------------------------

        /// <summary>A soft pool of light, brightest at its center, gone at <paramref name="r"/> (added).</summary>
        public void Glow(float cx, float cy, float r, Color c, float strength)
        {
            Bounds(cx - r, cy - r, cx + r, cy + r, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    float d = Mathf.Sqrt((px + 0.5f - cx) * (px + 0.5f - cx) + (py + 0.5f - cy) * (py + 0.5f - cy)) / r;
                    if (d >= 1) continue;
                    float f = (1 - d) * (1 - d);
                    Add(px, py, c, f * strength);
                }
        }

        /// <summary>
        /// A beam from a lamp at (<paramref name="x"/>, <paramref name="y0"/>), <paramref name="topHalf"/> wide there, spreading
        /// to <paramref name="bottomHalf"/> at <paramref name="y1"/>; soft at its sides and fading as it falls (added).
        /// </summary>
        public void Beam(float x, float y0, float topHalf, float y1, float bottomHalf, Color c, float strength)
        {
            Bounds(x - bottomHalf - topHalf, y0, x + bottomHalf + topHalf, y1, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
            {
                float t = Mathf.Clamp01((py - y0) / Mathf.Max(1, y1 - y0));
                if (py < y0 || py > y1) continue;
                float half = Mathf.Lerp(topHalf, bottomHalf, t);
                float fall = (1 - t * 0.75f);
                for (int px = ax; px <= bx; px++)
                {
                    float across = Mathf.Abs(px + 0.5f - x) / Mathf.Max(1, half);
                    if (across >= 1) continue;
                    float side = 1 - across * across;
                    Add(px, py, c, side * fall * strength);
                }
            }
        }

        // ---- Materials and finish ------------------------------------------------------------------------------------

        /// <summary>A texture laid over a box, tiled every <paramref name="tileW"/> × <paramref name="tileH"/> pixels, tinted.</summary>
        public void Tile(Color[] texture, int texW, int texH, float x, float y, float w, float h, float tileW, float tileH, Color tint, float alpha = 1)
        {
            Bounds(x, y, x + w, y + h, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    if (px < x || px >= x + w || py < y || py >= y + h) continue;
                    var t = Bilinear(texture, texW, texH, (px - x) / tileW, 1 - (py - y) / tileH);
                    Over(px, py, t * tint, alpha * t.a);
                }
        }

        /// <summary>A texture's pixels (rows bottom-up, as Unity gives them) sampled between texels at (u, v), wrapping.</summary>
        private static Color Bilinear(Color[] t, int w, int h, float u, float v)
        {
            float fx = u * w - 0.5f, fy = v * h - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            Color P(int x, int y) => t[((y % h) + h) % h * w + ((x % w) + w) % w];
            return Color.LerpUnclamped(Color.LerpUnclamped(P(x0, y0), P(x0 + 1, y0), tx), Color.LerpUnclamped(P(x0, y0 + 1), P(x0 + 1, y0 + 1), tx), ty);
        }

        // Classic Perlin noise in 0..1 (a fixed permutation), so painting needs no Unity call.
        private static readonly int[] Perm = BuildPerm();
        private static int[] BuildPerm()
        {
            var rng = new System.Random(20261106);
            var b = new int[256];
            for (int i = 0; i < 256; i++) b[i] = i;
            for (int i = 255; i > 0; i--) { int j = rng.Next(i + 1); (b[i], b[j]) = (b[j], b[i]); }
            var p = new int[512];
            for (int i = 0; i < 512; i++) p[i] = b[i & 255];
            return p;
        }
        private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        private static float Grad(int hash, float x, float y) => (hash & 3) switch { 0 => x + y, 1 => -x + y, 2 => x - y, _ => -x - y };
        private static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            int X = xi & 255, Y = yi & 255;
            x -= xi; y -= yi;
            float u = Fade(x), v = Fade(y);
            int a = Perm[X] + Y, b = Perm[X + 1] + Y;
            float top = Grad(Perm[a], x, y) + (Grad(Perm[b], x - 1, y) - Grad(Perm[a], x, y)) * u;
            float bottom = Grad(Perm[a + 1], x, y - 1) + (Grad(Perm[b + 1], x - 1, y - 1) - Grad(Perm[a + 1], x, y - 1)) * u;
            return Mathf.Clamp01((top + (bottom - top) * v) * 0.5f + 0.5f);
        }

        /// <summary>A grain over a box: each pixel darkened or lightened by up to <paramref name="amount"/> (Perlin, at a scale).</summary>
        public void Grain(float x, float y, float w, float h, float amount, float scale, float seed)
        {
            Bounds(x, y, x + w, y + h, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    if (px < x || px >= x + w || py < y || py >= y + h) continue;
                    float n = (Noise(seed + px / scale, seed * 0.7f + py / scale) - 0.5f) * 2 * amount;
                    int i = py * W + px;
                    var p = _px[i];
                    _px[i] = new Color(Mathf.Clamp01(p.r * (1 + n)), Mathf.Clamp01(p.g * (1 + n)), Mathf.Clamp01(p.b * (1 + n)), 1);
                }
        }

        /// <summary>Rows <paramref name="y0"/> to <paramref name="y1"/> scaled from brightness <paramref name="from"/> to <paramref name="to"/>.</summary>
        public void Shade(float y0, float y1, float from, float to)
        {
            for (int y = Mathf.Max(0, (int)y0); y < Mathf.Min(H, Mathf.CeilToInt(y1)); y++)
            {
                float k = Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, (y - y0) / Mathf.Max(1, y1 - y0)));
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    var p = _px[i];
                    _px[i] = new Color(p.r * k, p.g * k, p.b * k, 1);
                }
            }
        }

        /// <summary>An oval of shade (the dark a portrait stands in): darkest at its center by <paramref name="strength"/>, gone at its rim.</summary>
        public void Shadow(float cx, float cy, float rx, float ry, float strength)
        {
            Bounds(cx - rx, cy - ry, cx + rx, cy + ry, out int ax, out int ay, out int bx, out int by);
            for (int py = ay; py <= by; py++)
                for (int px = ax; px <= bx; px++)
                {
                    float u = (px + 0.5f - cx) / rx, v = (py + 0.5f - cy) / ry, d = Mathf.Sqrt(u * u + v * v);
                    if (d >= 1) continue;
                    float k = 1 - strength * Mathf.SmoothStep(0, 1, 1 - d);
                    int i = py * W + px;
                    var p = _px[i];
                    _px[i] = new Color(p.r * k, p.g * k, p.b * k, 1);
                }
        }

        /// <summary>
        /// The finish every backdrop gets: the corners darkened (<paramref name="vignette"/>), everything scaled to
        /// <paramref name="brightness"/>, and one soft blur pass, so it sits behind the dialogue box and the portraits.
        /// </summary>
        public void Finish(float vignette, float brightness)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W - 0.5f, v = (y + 0.5f) / H - 0.45f;
                    float k = brightness * (1 - vignette * Mathf.Clamp01((u * u * 1.6f + v * v * 2.2f) * 1.6f));
                    int i = y * W + x;
                    var p = _px[i];
                    _px[i] = new Color(p.r * k, p.g * k, p.b * k, 1);
                }
            // One 3 × 3 box blur, in place: soft, as if just out of focus. Two rows of the unblurred image are kept (the one
            // above and this one), so no second full buffer.
            var above = new Color[W];
            var row = new Color[W];
            System.Array.Copy(_px, 0, above, 0, W);
            for (int y = 1; y < H - 1; y++)
            {
                System.Array.Copy(_px, y * W, row, 0, W);
                for (int x = 1; x < W - 1; x++)
                {
                    Color s = default;
                    for (int dx = -1; dx <= 1; dx++) s += above[x + dx] + row[x + dx] + _px[(y + 1) * W + x + dx];
                    s /= 9f;
                    s.a = 1;
                    _px[y * W + x] = s;
                }
                (above, row) = (row, above);
            }
        }

        /// <summary>The canvas as texture pixels (row 0 at the bottom, as Unity has them), opaque.</summary>
        public Color32[] ToPixels()
        {
            var pixels = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = _px[y * W + x];
                    c.a = 1;
                    pixels[(H - 1 - y) * W + x] = c;
                }
            return pixels;
        }

        /// <summary>A small map of brightness (Rec. 709 luma), <paramref name="w"/> × <paramref name="h"/>, rows top-down: each cell its block's mean.</summary>
        public float[] Luma(int w, int h)
        {
            var luma = new float[w * h];
            int bw = W / w, bh = H / h;
            for (int cy = 0; cy < h; cy++)
                for (int cx = 0; cx < w; cx++)
                {
                    float sum = 0;
                    for (int y = cy * bh; y < (cy + 1) * bh; y++)
                        for (int x = cx * bw; x < (cx + 1) * bw; x++)
                        {
                            var c = _px[y * W + x];
                            sum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                        }
                    luma[cy * w + cx] = sum / (bw * bh);
                }
            return luma;
        }
    }
}
