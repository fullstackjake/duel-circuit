using System;
using System.Threading.Tasks;
using UnityEngine;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// How a copy's grade shows on its picture: a cut-back corner at Near Mint; nicks along the edges (gathered near the
    /// corners), a slightly off-centre print and the first scratches at Excellent; dulled gloss, dents, grime and fade at
    /// Played; creases, chips and stains at Damaged. Gem Mint shows none, and Mint none either, except that some slabbed
    /// 9s carry a faint print line (as on real cards, where one caps the grade at 9; see <see cref="PrintLine"/>).
    /// <para>
    /// On the board a worn card reads by its dulled gloss, its age and its corners; the finer wear (nicks, scratches,
    /// dents, print lines) shows only at detail size (owner decision 6). Scratches and dents show only inside the light's
    /// reflection, as on a real card.
    /// </para>
    /// <para>
    /// One wear atlas (512², generated in code at startup: the game ships no image assets) holds the shapes; each copy's
    /// seed picks where in it the card sits, which corners are worn and where a crease runs, so two copies at the same
    /// grade look different and the same copy always looks the same. The shader (CardFoil, keyword _WEAR) keeps the
    /// name, stars and text box within ±12% of their brightness, so wear never makes a card hard to read.
    /// </para>
    /// </summary>
    public static class CardWear
    {
        private const string PrefKey = "grades";
        private const int Size = 512;
        private const uint AtlasSeed = 0x9E3779B9;
        /// <summary>Scratch strokes in the atlas (a grade shows the first N).</summary>
        private const int Strokes = 32;

        public enum Display
        {
            /// <summary>Wear on the pictures, plus labels.</summary>
            Full,
            /// <summary>Labels and slabs only; pictures look as they did before grades.</summary>
            LabelOnly,
        }

        public static Display? Override { get; set; }

        /// <summary>The "Card grades" setting (remembered between runs).</summary>
        public static Display Mode
        {
            get => Override ?? (Display)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, (int)Display.Full), 0, 1);
            set
            {
                PlayerPrefs.SetInt(PrefKey, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static string ModeLabel => Mode == Display.Full ? "Card grades: full" : "Card grades: label only";

        public static void CycleMode()
        {
            Override = null;
            Mode = Mode == Display.Full ? Display.LabelOnly : Display.Full;
        }

        /// <summary>
        /// Whether this look gets wear drawn: a copy of yours at Near Mint or below, or a slabbed Mint foil with a print
        /// line (<paramref name="foiled"/>: it's drawn with a foil finish, which a print line needs), with the setting on
        /// and the atlas made.
        /// </summary>
        public static bool Shows(CopyLook look, bool foiled) =>
            look.IsCopy && (look.Grade <= 8 || (foiled && PrintLine(look) > 0)) && Mode == Display.Full && Ready;

        /// <summary>
        /// A foil copy's print line, 0 for none: seeded, about three in ten. Only where the grade anyone can see allows it:
        /// a copy shown at Near Mint or below, or a slab of exactly 9 (a print line keeps a card from a 10). Never on a Raw
        /// copy that reads Mint (a Raw 9 and a Raw 10 must look alike, or the line would give the hidden grade away), never
        /// on a slab 10, and never on a card with no foil.
        /// </summary>
        public static float PrintLine(CopyLook look)
        {
            if (!look.IsCopy || Rarities.FinishOf(look.Rarity).IsPlain) return 0;
            if (look.Shown > 8 && look.Slab != 9) return 0;
            float chance = look.Slab == 9 ? 0.25f : 0.3f;
            float roll = Hash.Mix(look.Seed, 20) / (float)uint.MaxValue;
            return roll < chance ? 0.55f + 0.45f * Hash.Mix(look.Seed, 21) / (float)uint.MaxValue : 0;
        }

        // ---- The atlas ----------------------------------------------------------------------------------

        private static Texture2D? _atlas;
        private static Task<Color32[]>? _making;

        /// <summary>Starts making the atlas off the main thread (call once at startup); cards drawn before it's ready show no wear.</summary>
        public static void Prepare()
        {
            if (_atlas == null && _making == null) _making = Task.Run(Generate);
        }

        public static bool Ready => Atlas != null;

        /// <summary>The atlas, once made: R fibre noise, G scratch rank, B grime, A stains.</summary>
        public static Texture2D? Atlas
        {
            get
            {
                if (_atlas != null) return _atlas;
                if (_making == null || !_making.IsCompleted) return null;
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, linear: true)
                {
                    name = "Card wear atlas", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 0,
                };
                texture.SetPixelData(_making.Result, 0);
                texture.Apply(true, makeNoLongerReadable: true);
                _making = null;
                return _atlas = texture;
            }
        }

        /// <summary>For checks: makes the atlas now (blocking) if it isn't ready.</summary>
        public static void PrepareNow()
        {
            Prepare();
            _making?.Wait();
            _ = Atlas;
        }

        private static Color32[] Generate()
        {
            var r = new float[Size * Size];
            var g = new float[Size * Size];
            var b = new float[Size * Size];
            var a = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    r[i] = 0.65f * Noise(x, y, 64, 1) + 0.35f * Noise(x, y, 128, 2);
                    float f = 0, amp = 0.5f, total = 0;
                    for (int o = 0; o < 4; o++)
                    {
                        f += amp * Noise(x, y, 4 << o, 10u + (uint)o);
                        total += amp;
                        amp *= 0.5f;
                    }
                    b[i] = f / total;
                }
            Scratches(g);
            Stains(a, r);
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(Byte(r[i]), Byte(g[i]), Byte(b[i]), Byte(a[i]));
            return pixels;
        }

        private static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255), 0, 255);

        /// <summary>Tileable value noise with <paramref name="cells"/> cells across the atlas (0..1).</summary>
        private static float Noise(int x, int y, int cells, uint salt)
        {
            float fx = (float)x / Size * cells, fy = (float)y / Size * cells;
            int x0 = (int)fx, y0 = (int)fy;
            float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
            float At(int cx, int cy) => Hash.Mix(AtlasSeed + salt, (uint)(cx % cells), (uint)(cy % cells)) / (float)uint.MaxValue;
            float top = Mathf.Lerp(At(x0, y0), At(x0 + 1, y0), tx);
            float bottom = Mathf.Lerp(At(x0, y0 + 1), At(x0 + 1, y0 + 1), tx);
            return Mathf.Lerp(top, bottom, ty);
        }

        private static float Smooth(float t) => t * t * (3 - 2 * t);

        private static float Rand(uint salt, uint i) => Hash.Mix(AtlasSeed, salt, i) / (float)uint.MaxValue;

        /// <summary>
        /// 32 slightly bowed strokes, each at its rank (1 for the first, falling by 1/32): a grade shows the first N.
        /// The early ones are short scuffs; later ones run longer.
        /// </summary>
        private static void Scratches(float[] g)
        {
            for (int s = 0; s < Strokes; s++)
            {
                float level = 1 - s / (float)Strokes;
                float length = (0.03f + 0.24f * s / (Strokes - 1f)) * Size;
                var start = new Vector2(Rand(100, (uint)s) * Size, Rand(101, (uint)s) * Size);
                float angle = Rand(102, (uint)s) * Mathf.PI * 2;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var normal = new Vector2(-dir.y, dir.x);
                float bend = (Rand(103, (uint)s) - 0.5f) * 0.12f * length;
                int steps = Mathf.CeilToInt(length * 2);
                for (int k = 0; k <= steps; k++)
                {
                    float t = (float)k / steps;
                    var point = start + dir * (t * length) + normal * (bend * 4 * t * (1 - t));
                    for (int oy = -2; oy <= 2; oy++)
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            int px = Mathf.FloorToInt(point.x) + ox, py = Mathf.FloorToInt(point.y) + oy;
                            float d = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f), point);
                            float v = d <= 1 ? 1 : Mathf.Clamp01(2 - d); // 2 texels solid, a 1-texel ramp
                            if (v <= 0) continue;
                            int i = ((py % Size + Size) % Size) * Size + (px % Size + Size) % Size;
                            g[i] = Mathf.Max(g[i], level * v);
                        }
                }
            }
        }

        /// <summary>Five blobs with a darker tide ring at the rim, their edges wobbled by the fibre noise.</summary>
        private static void Stains(float[] a, float[] fibre)
        {
            for (int s = 0; s < 5; s++)
            {
                var center = new Vector2(Rand(200, (uint)s) * Size, Rand(201, (uint)s) * Size);
                float radius = 30 + Rand(202, (uint)s) * 40;
                int reach = Mathf.CeilToInt(radius * 1.3f);
                for (int oy = -reach; oy <= reach; oy++)
                    for (int ox = -reach; ox <= reach; ox++)
                    {
                        int px = ((Mathf.FloorToInt(center.x) + ox) % Size + Size) % Size, py = ((Mathf.FloorToInt(center.y) + oy) % Size + Size) % Size;
                        int i = py * Size + px;
                        float d = Mathf.Sqrt(ox * ox + oy * oy) / radius + (fibre[i] - 0.5f) * 0.35f;
                        float v = d < 0.85f ? 0.55f : d < 1 ? Mathf.Lerp(0.55f, 1, (d - 0.85f) / 0.15f) : d < 1.08f ? 1 - (d - 1) / 0.08f : 0;
                        a[i] = Mathf.Max(a[i], v);
                    }
            }
        }

        // ---- Per grade ---------------------------------------------------------------------------------

        /// <summary>
        /// Per grade, 9 down to 1: corners worn, strength, radius; edge nicks, width; chips; scratches, opacity; grime;
        /// stains; fade; desaturation; crease; gloss dulled; dents; print off-centre (share of the print, at most).
        /// </summary>
        private static readonly float[,] Table =
        {
            // n   s     r      nicks  w       chips  count opac   grime  stain  fade   desat  crease dull   dents centre
            { 0, 0,     0,      0,     0,      0,     0,    0,     0,     0,     0,     0,     0,     0,     0,    0      },     // 9
            { 1, 0.55f, 0.018f, 0,     0,      0,     0,    0,     0,     0,     0,     0,     0,     0,     0,    0.004f },     // 8
            { 2, 0.70f, 0.022f, 0.25f, 0.006f, 0,     1,    0.35f, 0,     0,     0,     0,     0,     0.05f, 0,    0.006f },     // 7
            { 2, 0.80f, 0.026f, 0.45f, 0.008f, 0,     3,    0.45f, 0,     0,     0.02f, 0.03f, 0,     0.14f, 0,    0.008f },     // 6
            { 3, 0.90f, 0.030f, 0.60f, 0.010f, 0,     5,    0.5f,  0.05f, 0,     0.03f, 0.05f, 0,     0.26f, 1,    0.010f },     // 5
            { 4, 1,     0.034f, 0.75f, 0.012f, 0,     9,    0.55f, 0.18f, 0,     0.07f, 0.12f, 0,     0.42f, 1,    0.013f },     // 4
            { 4, 1,     0.038f, 0.85f, 0.014f, 0.1f,  13,   0.6f,  0.30f, 0.10f, 0.11f, 0.20f, 0,     0.55f, 2,    0.016f },     // 3
            { 4, 1,     0.043f, 0.95f, 0.016f, 0.4f,  18,   0.65f, 0.38f, 0.35f, 0.14f, 0.26f, 0.55f, 0.66f, 3,    0.018f },     // 2
            { 4, 1,     0.050f, 1,     0.018f, 0.8f,  24,   0.7f,  0.45f, 0.60f, 0.18f, 0.32f, 0.85f, 0.78f, 3,    0.020f },     // 1
        };

        /// <summary>
        /// Sets the wear properties for a copy on a CardFoil material (the caller turns on _WEAR). <paramref name="foiled"/>:
        /// the card is drawn with a foil finish (a print line needs one).
        /// </summary>
        public static void Apply(Material material, CopyLook look, bool reducedMotion, bool foiled)
        {
            int row = Mathf.Clamp(9 - look.Grade, 0, 8);
            float T(int column) => Table[row, column];
            uint seed = look.Seed;
            float R(uint salt) => Hash.Mix(seed, salt) / (float)uint.MaxValue;

            material.SetTexture("_WearTex", Atlas);
            material.SetVector("_WearSeed", new Vector4(R(1), R(2), Mathf.Floor(R(3) * 4), reducedMotion ? 1 : 0));
            // Which corners: a seeded order of BL, BR, TL, TR; the first n get s, 0.7s, 0.5s, 0.4s.
            var order = new[] { 0, 1, 2, 3 };
            for (int i = 3; i > 0; i--)
            {
                int j = (int)(Hash.Mix(seed, 4, (uint)i) % (uint)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            var corners = new float[4];
            float[] falloff = { 1, 0.7f, 0.5f, 0.4f };
            for (int i = 0; i < (int)T(0); i++) corners[order[i]] = T(1) * falloff[i];
            material.SetVector("_WearCorners", new Vector4(corners[0], corners[1], corners[2], corners[3]));
            material.SetVector("_WearEdge", new Vector4(T(2), T(3), T(4), T(5)));
            material.SetVector("_WearSurface", new Vector4(T(6) / Strokes, T(7), T(8), T(9)));
            material.SetVector("_WearAge", new Vector4(T(10), T(11), T(12), look.Grade <= 6 ? 1 : 0));
            // The crease: a fold across a corner at grade 2, a bend across the card at grade 1.
            const float aspect = 0.686f;
            Vector4 crease;
            if (look.Grade == 2)
            {
                int corner = order[0];
                float cx = (corner & 1) == 0 ? 0 : aspect, cy = corner < 2 ? 0 : 1;
                float inset = 0.12f + R(5) * 0.08f;
                var toward = new Vector2(cx == 0 ? 1 : -1, cy == 0 ? 1 : -1).normalized;
                var point = new Vector2(cx, cy) + toward * inset;
                float angle = Mathf.Atan2(toward.y, toward.x) + Mathf.PI / 2 + (R(6) - 0.5f) * 0.52f;
                crease = new Vector4(point.x, point.y, angle, 0.25f);
            }
            else if (look.Grade <= 1)
            {
                bool across = R(5) < 0.5f;
                float at = 0.35f + R(6) * 0.3f, tilt = (R(7) - 0.5f) * 0.28f;
                crease = across ? new Vector4(aspect / 2, at, tilt, 2) : new Vector4(at * aspect, 0.5f, Mathf.PI / 2 + tilt, 2);
            }
            else crease = Vector4.zero;
            material.SetVector("_WearCrease", crease);
            // Gloss, dents, and how far off-centre the print is (a seeded share of the grade's most, in a seeded
            // direction; the print is 0.91 x 0.936 of the card), plus a print line on some foil copies.
            material.SetVector("_WearGloss", new Vector4(T(13), T(14), 0.35f + 0.3f * R(9), 0));
            float off = T(15) * (0.5f + 0.5f * R(10)), heading = R(11) * Mathf.PI * 2;
            float line = foiled ? PrintLine(look) : 0;
            material.SetVector("_WearCenter", new Vector4(Mathf.Cos(heading) * off * 0.91f, Mathf.Sin(heading) * off * 0.936f,
                0.16f + 0.68f * R(12), line));
        }
    }
}
