using System;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The Back Alley's materials, painted in code like <see cref="BoardFx.Textures"/> and cached (docs/dev/ALLEY-SPEC-2026-11-01.md
    /// section 13; no images): brick (running bond, mortar lines, a wet sheen low on the wall), chain-link (a diamond lattice,
    /// laid at 20% alpha), tape (a torn strip, two of them laid at a slight angle) and the hooded silhouette that stands in
    /// for a duelist without a portrait. Used by the alley map's backdrop, a table panel's header and the van's frame.
    /// </summary>
    public static class AlleyMaterials
    {
        /// <summary>The chain-link fence's alpha over whatever it hangs in front of.</summary>
        public const float ChainLinkAlpha = 0.2f;

        private static Texture2D? _brick, _chainLink, _tape, _hood;

        /// <summary>A tileable brick wall (wrap Repeat): dark, worn brick in running bond, sooty mortar. <see cref="Wall"/> adds the wet sheen.</summary>
        public static Texture2D Brick => _brick ??= Paint(256, 128, TextureWrapMode.Repeat, (u, v) =>
        {
            // Four courses of two bricks each, every other course offset by half a brick.
            float course = v * 4, row = Mathf.Floor(course), offset = ((int)row & 1) == 0 ? 0 : 0.5f;
            float across = u * 2 + offset, col = Mathf.Floor(across);
            float fy = course - row, fx = across - col;
            // Mortar: within a hair of a brick's edge (in pixels: about 3 of a 64 x 32 brick).
            float mortar = Mathf.Clamp01(Mathf.Max(1 - Mathf.Min(fy, 1 - fy) * 32 / 3f, 1 - Mathf.Min(fx, 1 - fx) * 128 / 3f));
            // Each brick its own shade, mottled.
            float shade = Hash(Mathf.Repeat(col, 2), row) * 0.35f + Mathf.PerlinNoise(u * 24, v * 12) * 0.25f;
            var brick = Color.Lerp(Ui.Hex("#2E1712"), Ui.Hex("#5A2C20"), shade);
            var c = Color.Lerp(brick, Ui.Hex("#15110F"), mortar);
            c.a = 1;
            return c;
        });

        /// <summary>A tileable chain-link fence: white wire in a diamond lattice on clear (tint it, and lay it at <see cref="ChainLinkAlpha"/>).</summary>
        public static Texture2D ChainLink => _chainLink ??= Paint(64, 64, TextureWrapMode.Repeat, (u, v) =>
        {
            // Two families of diagonal wires, one diamond per tile.
            float a = Mathf.Repeat(u + v, 1), b = Mathf.Repeat(u - v, 1);
            float wire = Mathf.Max(Mathf.Clamp01(1 - Mathf.Abs(a - 0.5f) * 64 / 2.2f), Mathf.Clamp01(1 - Mathf.Abs(b - 0.5f) * 64 / 2.2f));
            return new Color(1, 1, 1, wire);
        });

        /// <summary>A strip of tape, white (tint it), its ends torn ragged, a little see-through.</summary>
        public static Texture2D Tape => _tape ??= Paint(256, 40, TextureWrapMode.Clamp, (u, v) =>
        {
            // The torn ends: a jagged edge a few pixels deep, different at each end.
            float tearLeft = 0.03f + 0.025f * Hash(Mathf.Floor(v * 10), 1), tearRight = 0.97f - 0.025f * Hash(Mathf.Floor(v * 10), 2);
            float inside = u > tearLeft && u < tearRight ? 1 : 0;
            // Creased and grimy along its length.
            float grime = 0.85f + 0.15f * Mathf.PerlinNoise(u * 18, v * 3);
            return new Color(grime, grime, grime, 0.88f * inside);
        });

        /// <summary>
        /// The hooded figure (the code-drawn stand-in for art/portraits/alley-hood.png): a white hood and shoulders, the face
        /// in its shadow nearly black, on clear. Tinted per duelist, so the hood takes the tint and the face stays dark.
        /// </summary>
        public static Texture2D Hood => _hood ??= Paint(200, 240, TextureWrapMode.Clamp, (u, v) =>
        {
            float x = (u - 0.5f) * 2, y = v * 2 - 1; // -1..1, y up
            // Shoulders: a wide ellipse cut off at the bottom.
            float shoulders = Inside((x / 0.95f) * (x / 0.95f) + ((y + 0.95f) / 0.55f) * ((y + 0.95f) / 0.55f));
            // The hood: an arch, rounded on top, widening down to the shoulders.
            float hoodHalf = 0.42f + Mathf.Clamp01(-y) * 0.18f;
            float hood = Inside((x / hoodHalf) * (x / hoodHalf) + Mathf.Pow(Mathf.Max(0, y - 0.05f) / 0.72f, 2)) * (y > -0.62f ? 1 : 0);
            // The face in its shadow: an oval inside the hood.
            float face = Inside((x / 0.25f) * (x / 0.25f) + ((y - 0.02f) / 0.33f) * ((y - 0.02f) / 0.33f));
            float alpha = Mathf.Max(shoulders, hood);
            float tone = hood > 0 ? 1 : 0.72f;
            tone = Mathf.Lerp(tone, 0.08f, face * hood);
            return new Color(tone, tone, tone, alpha);
        });

        // ---- Laid out ------------------------------------------------------------------------------------------------

        /// <summary>
        /// The alley map's backdrop: a brick wall darkened toward the top, a stretch of chain-link fence over its right half,
        /// and two strips of tape across a corner. Under everything (no raycasts).
        /// </summary>
        public static void Backdrop(RectTransform parent, float x, float y, float w, float h, Color accent)
        {
            Wall(parent, x, y, w, h, 0.62f);
            // Darker at the top, as a wall going up into the night.
            Raw(Ui.Box(parent, x, y, w, Mathf.Min(h * 0.5f, 600)), Fade, UiTheme.BackAlley.Deep.WithAlpha(0.7f));
            var fence = Raw(Ui.Box(parent, x + w * 0.45f, y, w * 0.55f, h), ChainLink, Color.white.WithAlpha(ChainLinkAlpha));
            fence.uvRect = new Rect(0, 0, w * 0.55f / 48f, h / 48f);
            TapeStrips(parent, x + w - 250, y + 6, accent);
        }

        /// <summary>A panel's header band in brick (a table's title sits on it), dimmed so the title reads, edged with tape.</summary>
        public static void Header(RectTransform parent, float x, float y, float w, float h, Color accent)
        {
            Wall(parent, x, y, w, h, 0.55f);
            Ui.Image(Ui.Box(parent, x, y, w, h), UiTheme.BackAlley.Deep.WithAlpha(0.5f)).raycastTarget = false;
            TapeStrips(parent, x + w - 250, y + 2, accent);
        }

        /// <summary>
        /// Brick over a box at a brightness, with the wet sheen: the lower part of the wall catches the light, brighter and
        /// cooler toward the ground.
        /// </summary>
        public static void Wall(RectTransform parent, float x, float y, float w, float h, float brightness)
        {
            var wall = Raw(Ui.Box(parent, x, y, w, h), Brick, new Color(brightness, brightness * 0.97f, brightness * 0.97f));
            wall.uvRect = new Rect(0, 0, w / 256f, h / 128f);
            float wet = Mathf.Min(h * 0.45f, 400);
            Raw(Ui.Box(parent, x, y + h - wet, w, wet), Fade, Ui.Hex("#7A7690").WithAlpha(0.22f)).uvRect = new Rect(0, 1, 1, -1); // clear at the top
        }

        /// <summary>
        /// The van's frame: brick showing around the edges of a box (<paramref name="border"/> px), the content's ground
        /// inside. Returns the inner box.
        /// </summary>
        public static RectTransform Frame(RectTransform parent, float x, float y, float w, float h, float border, Color ground)
        {
            Wall(parent, x, y, w, h, 0.5f);
            var inner = Ui.Box(parent, x + border, y + border, w - border * 2, h - border * 2);
            Ui.Image(inner, ground).raycastTarget = false;
            return inner;
        }

        /// <summary>Two torn strips of tape crossing at a slight angle (a corner's worth, about 240 × 60).</summary>
        public static void TapeStrips(RectTransform parent, float x, float y, Color tint)
        {
            for (int i = 0; i < 2; i++)
            {
                var strip = Raw(Ui.Box(parent, x + i * 22, y + 8 + i * 18, 220, 22), Tape, Color.Lerp(tint, Ui.Hex("#E8D9A8"), i == 0 ? 0.25f : 0.65f).WithAlpha(0.85f));
                strip.rectTransform.localRotation = Quaternion.Euler(0, 0, i == 0 ? 3.5f : -2.5f);
            }
        }

        /// <summary>
        /// A hooded figure in a duelist's tint inside <paramref name="box"/>: the owner's art/portraits/alley-hood.png (white,
        /// transparent) if it's there, else the code-drawn <see cref="Hood"/>. As large as fits, standing on the box's bottom.
        /// </summary>
        public static RawImage Silhouette(RectTransform box, Color tint)
        {
            Texture texture = Portraits.Get("alley-hood") is Texture2D art ? art : Hood;
            var size = box.rect.size == Vector2.zero ? box.sizeDelta : box.rect.size;
            float scale = Mathf.Min(size.x / texture.width, size.y / texture.height);
            var rt = Ui.Rect("Silhouette", box);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(texture.width * scale, texture.height * scale);
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = tint;
            image.raycastTarget = false;
            return image;
        }

        // ---- Painting ------------------------------------------------------------------------------------------------

        private static Texture2D? _fade;
        /// <summary>A vertical fade: opaque at the top, clear at the bottom (tint it).</summary>
        private static Texture2D Fade => _fade ??= Paint(4, 64, TextureWrapMode.Clamp, (u, v) => new Color(1, 1, 1, Mathf.SmoothStep(0, 1, v)));

        private static RawImage Raw(RectTransform rt, Texture texture, Color color)
        {
            var image = rt.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A texture from a color at each (u, v) in 0..1 (v up).</summary>
        private static Texture2D Paint(int w, int h, TextureWrapMode wrap, Func<float, float, Color> at)
        {
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = wrap, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = at((x + 0.5f) / w, (y + 0.5f) / h);
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static float Inside(float q) => Mathf.Clamp01((1 - q) * 18);
        private static float Hash(float x, float y) { var n = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f; return n - Mathf.Floor(n); }
    }
}
