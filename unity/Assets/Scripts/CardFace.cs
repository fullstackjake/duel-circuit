using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>The four plain-card layouts, by the card's size in layout units.</summary>
    public enum CardLayout { Thumb, Compact, Standard, Full }

    /// <summary>What the face must leave room for: nothing, or the deck editor's limit badge and in-deck count.</summary>
    public enum CardFit { Normal, Badged }

    /// <summary>
    /// What a plain face shows at its width on screen (docs/dev/TYPE-AND-FONTS-SPEC-2026-11-21.md section 2): Compact drops to
    /// its name alone under 52 px, Standard drops its text box under 200 px, so nothing on a card is under 7 px (8 for text).
    /// </summary>
    public enum FaceVariant { Thumb, Compact, CompactNameOnly, Standard, StandardNoText, Full }

    /// <summary>
    /// Every card face in the game: the player's picture when there is one, else the plain card (the game's own drawn face,
    /// docs/dev/PLAIN-CARDS-SPEC-2026-11-20.md). Never null for a card the game has data for; null only for code 0.
    /// Plain cards are textures drawn once per card, layout, fit and resolution by <see cref="PlainCards"/> and kept in a
    /// least-recently-used cache bounded by bytes. Pictures come from <see cref="CardArt"/> untouched.
    /// </summary>
    public static class CardFace
    {
        /// <summary>The plain cache's memory cap (all mips counted).</summary>
        public const long CacheCap = 96L * 1024 * 1024;

        /// <summary>The most drawing a frame does before further faces get their blank and wait in the queue (ms).</summary>
        public const double FrameBudgetMs = 6;

        /// <summary>A plain texture's width to height (the shader's card shape).</summary>
        public const float Aspect = 0.686f;

        private static readonly int[] Ladder = { 64, 96, 128, 192, 288, 432, 648, 972 };
        private const string Prefix = "plain:";

        /// <summary>The variant a plain face takes at <paramref name="screenWidthPx"/> pixels wide on screen (section 2).</summary>
        public static FaceVariant VariantFor(CardLayout layout, float screenWidthPx) => layout switch
        {
            CardLayout.Thumb => FaceVariant.Thumb,
            CardLayout.Compact => screenWidthPx < 52 ? FaceVariant.CompactNameOnly : FaceVariant.Compact,
            CardLayout.Standard => screenWidthPx < 200 ? FaceVariant.StandardNoText : FaceVariant.Standard,
            _ => FaceVariant.Full,
        };

        /// <summary>
        /// Each text on a drawn plain face (name, chip, type line, card text, pendulum text, stats, rating) with its size on
        /// screen in pixels at the width it was drawn for, and whether it was cut ("…"). Empty for a picture or a face not
        /// drawn yet.
        /// </summary>
        public static IReadOnlyList<(string element, string text, float px, bool cut)> DebugTexts(Texture texture) =>
            texture != null && Elements.TryGetValue(texture, out var list) ? list : (IReadOnlyList<(string, string, float, bool)>)Array.Empty<(string, string, float, bool)>();

        /// <summary>A plain texture's variant (null for a picture).</summary>
        public static FaceVariant? VariantOf(Texture? texture)
        {
            if (texture == null || !IsPlain(texture)) return null;
            if (ByTexture.TryGetValue(texture, out var entry)) return entry.Key.Variant;
            var parts = texture.name.Split(':');
            return parts.Length >= 6 && Enum.TryParse(parts[5], out FaceVariant v) ? v : (FaceVariant?)null;
        }

        public static CardLayout LayoutFor(Vector2 layoutSize) =>
            layoutSize.x < 60 ? CardLayout.Thumb : layoutSize.x < 140 ? CardLayout.Compact : layoutSize.x < 280 ? CardLayout.Standard : CardLayout.Full;

        /// <summary>The face texture for a card at a size in layout units. <paramref name="full"/> asks for the whole
        /// picture (CardArt.GetFull) where one exists, for the big views.</summary>
        public static Texture? Picture(uint code, CardInfo? info, Vector2 layoutSize, CardFit fit = CardFit.Normal, bool full = false)
        {
            if (code == 0) return null;
            var art = CardView.Art;
            if (art != null)
            {
                var picture = full ? art.GetFull(code) : art.Get(code);
                if (picture != null) return picture;
            }
            var layout = LayoutFor(layoutSize);
            return PlainOnly(code, info, layout, PixelWidth(layout, layoutSize.x), fit);
        }

        /// <summary>
        /// The plain card alone (never a picture), for checks and the title wall's fill. <paramref name="pixelWidth"/> is its
        /// width on screen: it picks the face's variant and the least sizes its texts may take (section 2), and keys the
        /// cache (a Thumb, which has no text, by its texture width alone).
        /// </summary>
        public static Texture PlainOnly(uint code, CardInfo? info, CardLayout layout, int pixelWidth, CardFit fit = CardFit.Normal)
        {
            if (layout != CardLayout.Compact) fit = CardFit.Normal; // only Compact cards wear the editor's badges
            CheckFonts();
            int width = Snap(layout, pixelWidth);
            int screen = layout == CardLayout.Thumb ? 0 : Math.Max(1, pixelWidth);
            var variant = VariantFor(layout, pixelWidth);
            var key = new Key(code, info != null, layout, fit, width, variant, screen);
            if (Cache.TryGetValue(key, out var hit))
            {
                Lru.Remove(hit.Node);
                Lru.AddFirst(hit.Node);
                return hit.Texture;
            }
            int height = Mathf.RoundToInt(width / Aspect);
            var entry = new Entry { Key = key, Info = info };
            entry.Texture = PlainCards.NewTexture(width, height, $"{Prefix}{code}:{layout}:{fit}:{width}:{variant}:{screen}");
            entry.Bytes = PlainCards.BytesOf(entry.Texture);
            entry.Node = Lru.AddFirst(entry);
            Cache[key] = entry;
            ByTexture[entry.Texture] = entry;
            Bytes += entry.Bytes;
            if (Room()) Render(entry);
            else
            {
                // Over this frame's budget: its kind's blank now, the face itself in a later frame (same texture object).
                PlainCards.Blank(entry.Texture, info, layout, fit);
                entry.Queued = true;
                Queue.Enqueue(entry);
                Runner.Ensure();
            }
            Evict();
            return entry.Texture;
        }

        public static bool IsPlain(Texture? texture) => texture != null && texture.name.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>A plain texture's layout; null for a picture.</summary>
        public static CardLayout? LayoutOf(Texture? texture) => PlainLayout(texture, out var layout, out _) ? layout : (CardLayout?)null;

        /// <summary>A plain texture's layout and fit (false for a picture or nothing).</summary>
        internal static bool PlainLayout(Texture? texture, out CardLayout layout, out CardFit fit)
        {
            layout = CardLayout.Compact;
            fit = CardFit.Normal;
            if (texture == null || !IsPlain(texture)) return false;
            if (ByTexture.TryGetValue(texture, out var entry))
            {
                layout = entry.Key.Layout;
                fit = entry.Key.Fit;
                return true;
            }
            // Dropped from the cache but still shown (Invalidate keeps it until nothing does): read it from its name.
            var parts = texture.name.Split(':');
            return parts.Length >= 4 && Enum.TryParse(parts[2], out layout) && Enum.TryParse(parts[3], out fit);
        }

        /// <summary>Puts a face on <paramref name="image"/> (texture, tag; a plain card's uvRect reset) and returns it.
        /// Callers then call CardFoil.Apply as they do today.</summary>
        public static RawImage Draw(RawImage image, uint code, CardInfo? info, Vector2 layoutSize, CardFit fit = CardFit.Normal, bool full = false)
        {
            // The face for the card's real size on screen: a card under a scaled parent (a detail at 0.62) gets the layout and
            // variant that size takes.
            var texture = Picture(code, info, layoutSize * ScaleOf(image.rectTransform), fit, full);
            image.texture = texture;
            if (IsPlain(texture)) image.uvRect = new Rect(0, 0, 1, 1);
            Tag(image, code, layoutSize, fit, info);
            return image;
        }

        /// <summary>Tags a face drawn on <paramref name="image"/> (by <see cref="Draw"/> or a CardView) with what it shows.</summary>
        internal static void Tag(RawImage image, uint code, Vector2 layoutSize, CardFit fit, CardInfo? info = null)
        {
            image.TryGetComponent<FaceTag>(out var tag);
            if (image.texture == null)
            {
                if (tag != null) UnityEngine.Object.Destroy(tag);
                return;
            }
            if (tag == null) tag = image.gameObject.AddComponent<FaceTag>();
            tag.Image = image;
            // Only a face on an active object pins its texture (FaceTag.OnEnable/OnDisable): a hidden screen's plain faces may
            // be evicted, and are drawn again when it shows (FaceTag.OnEnable).
            tag.Code = code;
            tag.Info = info;
            // A plain face is drawn again at the width on screen it was drawn for (its variant and floors), not its texture's.
            tag.Width = ByTexture.TryGetValue(image.texture, out var drawn) ? drawn.Key.Screen : image.texture.width;
            tag.Plain = PlainLayout(image.texture, out var layout, out var plainFit);
            tag.Layout = tag.Plain ? layout : LayoutFor(layoutSize);
            tag.Fit = tag.Plain ? plainFit : fit;
        }

        /// <summary>The plain layout's window in UV (posters, cut-ins crop to it); a picture's art window otherwise
        /// (PosterView.ArtBox's values).</summary>
        public static Rect WindowUv(Texture texture)
        {
            if (PlainLayout(texture, out var layout, out var fit))
            {
                var w = PlainCards.FoilRects(layout, fit, VariantOf(texture)).art;
                if (layout == CardLayout.Thumb) w = new Vector4(0.2f, 0.5f - 0.3f * Aspect, 0.8f, 0.5f + 0.3f * Aspect);
                return new Rect(w.x, w.y, w.z - w.x, w.w - w.y);
            }
            return new Rect(21f / 177, 1 - 182f / 254, 136f / 177, 136f / 254);
        }

        /// <summary>A rarity's short mark: N, R, SR, UR, ScR, then initials (moved from CardDetail.Short).</summary>
        public static string RarityShort(Rarity rarity) => rarity switch
        {
            // One mark per rarity, the abbreviations players use where there is one (never two rarities alike, 5 letters at most).
            Rarity.Common => "N",
            Rarity.ShortPrint => "SP",
            Rarity.Rare => "R",
            Rarity.NormalParallelRare => "NPR",
            Rarity.RareParallelRare => "RPR",
            Rarity.SuperRare => "SR",
            Rarity.SuperParallelRare => "SPR",
            Rarity.StarfoilRare => "SFR",
            Rarity.MosaicRare => "MSR",
            Rarity.ShatterfoilRare => "SHR",
            Rarity.UltraRare => "UR",
            Rarity.UltraParallelRare => "UPR",
            Rarity.PharaohsRare => "PhR",
            Rarity.GoldRare => "GR",
            Rarity.PremiumGoldRare => "PGR",
            Rarity.PlatinumRare => "PlR",
            Rarity.SecretRare => "ScR",
            Rarity.PrismaticSecretRare => "PScR",
            Rarity.GoldSecretRare => "GScR",
            Rarity.ExtraSecretRare => "EScR",
            Rarity.PlatinumSecretRare => "PlScR",
            Rarity.UltimateRare => "UtR",
            Rarity.CollectorsRare => "CR",
            Rarity.GhostGoldRare => "GGR",
            Rarity.GhostRare => "GhR",
            Rarity.QuarterCenturySecretRare => "QCSR",
            Rarity.StarlightRare => "StR",
            _ => "?",
        };

        public static Color RarityColor(Rarity rarity) => rarity switch
        {
            Rarity.Common or Rarity.ShortPrint => Ui.Hex("#B8BCC6"),
            Rarity.Rare => Ui.Hex("#D8DCE6"),
            Rarity.SuperRare => Ui.Hex("#6FC3FF"),
            Rarity.UltraRare => Ui.Hex("#FFD45A"),
            Rarity.SecretRare => Ui.Hex("#FF8FE0"),
            _ => CardFoil.PrizeColor(rarity),
        };

        /// <summary>Empties the plain cache (the font changing; a check). Faces still on screen keep their texture until
        /// nothing shows it.</summary>
        public static void Invalidate()
        {
            // Faces still showing keep their texture and are drawn again (in later frames, through the queue); the rest go.
            var shown = InUse();
            Queue.Clear();
            Timings.Clear();
            foreach (var entry in new List<Entry>(Cache.Values))
            {
                Names.Remove(entry.Texture);
                Texts.Remove(entry.Texture);
                Elements.Remove(entry.Texture);
                if (shown.Contains(entry.Texture))
                {
                    entry.Queued = true;
                    Queue.Enqueue(entry);
                    continue;
                }
                Cache.Remove(entry.Key);
                ByTexture.Remove(entry.Texture);
                Lru.Remove(entry.Node);
                Bytes -= entry.Bytes;
                UnityEngine.Object.Destroy(entry.Texture);
            }
            foreach (var t in Retired) if (t != null) UnityEngine.Object.Destroy(t);
            Retired.Clear();
            if (Queue.Count > 0) Runner.Ensure();
        }

        // ---- For checks ----

        public static long DebugBytes => Bytes;
        public static int DebugQueued => Queue.Count;

        /// <summary>First draws since the cache was last emptied: their median and 95th percentile time, and how many.</summary>
        public static (double medianMs, double p95Ms, int drawn) DebugTimings
        {
            get
            {
                if (Timings.Count == 0) return (0, 0, 0);
                var sorted = new List<double>(Timings);
                sorted.Sort();
                double At(double q) => sorted[Mathf.Clamp(Mathf.CeilToInt((float)(q * sorted.Count)) - 1, 0, sorted.Count - 1)];
                return (At(0.5), At(0.95), sorted.Count);
            }
        }

        /// <summary>The name as laid out on a plain texture: its text with line breaks, size, rect (card fractions), and
        /// CardView.BrokenWords / SplitDigraphs problems found when it was drawn.</summary>
        public static PlainName? DebugName(Texture texture) => texture != null && Names.TryGetValue(texture, out var name) ? name : null;

        /// <summary>For checks: a plain texture's text box as drawn (its text, italic, the Pendulum effect's text, and
        /// whether every character shows).</summary>
        internal static PlainText? DebugText(Texture texture) => texture != null && Texts.TryGetValue(texture, out var text) ? text : null;

        /// <summary>Every tagged face under <paramref name="root"/> (active and on screen), and problems: a CardView that
        /// shows a face without a tag, a tagged face with no texture.</summary>
        public static (List<FaceTag> faces, List<string> problems) DebugAudit(Transform root)
        {
            var faces = new List<FaceTag>();
            var problems = new List<string>();
            foreach (var tag in root.GetComponentsInChildren<FaceTag>(false))
            {
                if (!tag.isActiveAndEnabled || !OnScreen((RectTransform)tag.transform)) continue;
                faces.Add(tag);
                var image = tag.GetComponent<RawImage>();
                if (image == null || image.texture == null) problems.Add($"a face tagged {tag.Code} ({tag.name}) has no texture");
            }
            foreach (var view in root.GetComponentsInChildren<CardView>(false))
                if (view.DebugFaceDrawn && OnScreen((RectTransform)view.transform) && view.GetComponentInChildren<FaceTag>() == null)
                    problems.Add($"a card ({view.name} at {view.transform.position}) shows a face without a tag");
            return (faces, problems);
        }

        /// <summary>Whether a face looks blank in a screenshot: 97% or more of a 24x24 sample grid within 24/255 of the
        /// median colour in every channel. The grid lies in the face's own (possibly rotated) rect, a little in from its
        /// edges; samples off the screen or outside a mask over the face are left out, so the visible part is judged. Under
        /// 30% of the grid visible: not judged (false).</summary>
        public static bool DebugFlat(Texture2D screenshot, RectTransform face)
        {
            var canvas = face.GetComponentInParent<Canvas>();
            Camera? camera = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            var clips = new List<Rect>();
            for (var p = face.parent; p != null; p = p.parent)
                if ((p.TryGetComponent<RectMask2D>(out var m2) && m2.enabled) || (p.TryGetComponent<Mask>(out var m) && m.enabled))
                    clips.Add(ScreenBox((RectTransform)p));
            var r = face.rect;
            const int n = 24;
            var samples = new List<Color32>(n * n);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    // In from the edges a little: the owner's border and the rounded corners are not the face.
                    var local = new Vector3(r.xMin + r.width * (0.04f + 0.92f * (i + 0.5f) / n), r.yMin + r.height * (0.04f + 0.92f * (j + 0.5f) / n), 0);
                    var p = RectTransformUtility.WorldToScreenPoint(camera, face.TransformPoint(local));
                    int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
                    if (x < 0 || y < 0 || x >= screenshot.width || y >= screenshot.height) continue;
                    bool clipped = false;
                    foreach (var c in clips) if (!c.Contains(p)) { clipped = true; break; }
                    if (!clipped) samples.Add(screenshot.GetPixel(x, y));
                }
            if (samples.Count < n * n * 3 / 10) return false;
            byte Median(Func<Color32, byte> channel)
            {
                var v = samples.ConvertAll(c => channel(c));
                v.Sort();
                return v[v.Count / 2];
            }
            byte rr = Median(c => c.r), g = Median(c => c.g), b = Median(c => c.b);
            int near = 0;
            foreach (var c in samples)
                if (Mathf.Abs(c.r - rr) <= 24 && Mathf.Abs(c.g - g) <= 24 && Mathf.Abs(c.b - b) <= 24) near++;
            return near >= 0.97f * samples.Count;
        }

        // ---- Inside ----

        private readonly struct Key : IEquatable<Key>
        {
            public readonly uint Code;
            public readonly bool Known;
            public readonly CardLayout Layout;
            public readonly CardFit Fit;
            public readonly int Width;
            /// <summary>The variant (section 2) and the width on screen (4 px steps) the face's texts are sized for.</summary>
            public readonly FaceVariant Variant;
            public readonly int Screen;

            public Key(uint code, bool known, CardLayout layout, CardFit fit, int width, FaceVariant variant, int screen)
            {
                Code = code;
                Known = known;
                Layout = layout;
                Fit = fit;
                Width = width;
                Variant = variant;
                Screen = screen;
            }

            public bool Equals(Key o) => Code == o.Code && Known == o.Known && Layout == o.Layout && Fit == o.Fit && Width == o.Width
                                         && Variant == o.Variant && Screen == o.Screen;
            public override bool Equals(object? obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => unchecked((int)Code * 397 ^ (Known ? 1 : 0) ^ ((int)Layout << 1) ^ ((int)Fit << 3) ^ (Width << 5)
                                                           ^ ((int)Variant << 17) ^ (Screen << 20));
        }

        private sealed class Entry
        {
            public Key Key;
            public CardInfo? Info;
            public Texture2D Texture = null!;
            public long Bytes;
            public LinkedListNode<Entry> Node = null!;
            public bool Queued;
        }

        private static readonly Dictionary<Key, Entry> Cache = new Dictionary<Key, Entry>();
        private static readonly LinkedList<Entry> Lru = new LinkedList<Entry>();
        private static readonly Dictionary<Texture, Entry> ByTexture = new Dictionary<Texture, Entry>();
        private static readonly Dictionary<Texture, PlainName> Names = new Dictionary<Texture, PlainName>();
        private static readonly Dictionary<Texture, PlainText> Texts = new Dictionary<Texture, PlainText>();
        private static readonly Dictionary<Texture, List<(string, string, float, bool)>> Elements = new Dictionary<Texture, List<(string, string, float, bool)>>();

        /// <summary>The fonts the faces were drawn with: when one changes (a missing file found, a new face), the cache and the
        /// studio's templates are emptied.</summary>
        private static readonly Font?[] _fonts = new Font?[4];
        private static readonly Face[] FontFaces = { Face.Body, Face.Strong, Face.Condensed, Face.Italic };

        private static void CheckFonts()
        {
            bool changed = false, first = _fonts[0] == null;
            for (int i = 0; i < FontFaces.Length; i++)
            {
                var f = Typography.Font(FontFaces[i]);
                if (!ReferenceEquals(f, _fonts[i])) { _fonts[i] = f; changed = true; }
            }
            if (!changed || first) return;
            PlainCards.ResetTemplates();
            Invalidate();
        }
        private static readonly Queue<Entry> Queue = new Queue<Entry>();
        /// <summary>Textures dropped by <see cref="Invalidate"/> while a face still showed them: destroyed once none does.</summary>
        private static readonly List<Texture2D> Retired = new List<Texture2D>();
        private static readonly List<double> Timings = new List<double>();
        private static long Bytes;
        private static int _frame = -1;
        private static double _spent;
        private static bool _warnedFull;

        /// <summary>A rect's scale against its root canvas (1 when it isn't under one yet).</summary>
        internal static float ScaleOf(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return 1;
            float root = canvas.rootCanvas.transform.lossyScale.y;
            return root > 0 && rt.lossyScale.y > 0 ? rt.lossyScale.y / root : 1;
        }

        /// <summary>The texture width for a card <paramref name="layoutWidth"/> layout units wide on this screen.</summary>
        internal static int PixelWidth(CardLayout layout, float layoutWidth)
        {
            // The canvases' scale (CanvasScaler, 1920x1080 reference, Expand): pixels, so Retina screens get sharper faces.
            // The width on screen (PlainOnly snaps it to a texture width, and picks the variant and floors from it).
            float scale = DebugScale ?? Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            return Mathf.CeilToInt(layoutWidth * scale - 0.01f);
        }

        /// <summary>Textures held for a while without a face showing them (a destroyed card's shards): until when.</summary>
        private static readonly Dictionary<Texture, float> Held = new Dictionary<Texture, float>();

        /// <summary>Keeps a plain texture in the cache for <paramref name="seconds"/> (real time) though no face shows it.</summary>
        public static void Hold(Texture texture, float seconds)
        {
            if (!IsPlain(texture)) return;
            float now = Time.realtimeSinceStartup;
            foreach (var key in new List<Texture>(Held.Keys)) if (key == null || Held[key] <= now) Held.Remove(key);
            Held[texture] = Mathf.Max(Held.TryGetValue(texture, out var until) ? until : 0, now + seconds);
        }

        /// <summary>For checks: the canvas scale to size faces for, in place of this window's (a 4K screen's, say).</summary>
        internal static float? DebugScale;

        /// <summary>The title wall's faces: never wider than this (it sits dimmed behind the logo), whatever the screen.</summary>
        public const int WallWidth = 288;

        /// <summary>A title-wall face: the picture, else the plain Standard card at most <see cref="WallWidth"/> wide.</summary>
        public static Texture? WallFace(uint code, CardInfo? info, Vector2 layoutSize)
        {
            if (code == 0) return null;
            var picture = CardView.Art?.Get(code);
            if (picture != null) return picture;
            var layout = LayoutFor(layoutSize);
            return PlainOnly(code, info, layout, Mathf.Min(WallWidth, PixelWidth(layout, layoutSize.x)));
        }

        private static int Snap(CardLayout layout, int pixels)
        {
            int cap = layout switch { CardLayout.Thumb => 96, CardLayout.Compact => 288, CardLayout.Standard => 648, _ => 972 };
            int width = Ladder[Ladder.Length - 1];
            foreach (var rung in Ladder)
                if (rung >= pixels) { width = rung; break; }
            return Mathf.Min(width, cap);
        }

        private static bool Room()
        {
            if (Time.frameCount != _frame)
            {
                _frame = Time.frameCount;
                _spent = 0;
            }
            // Never in the middle of a canvas rebuild (a face asked for from layout code): queue it.
            if (UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingLayout() || UnityEngine.UI.CanvasUpdateRegistry.IsRebuildingGraphics()) return false;
            return _spent < FrameBudgetMs;
        }

        private static void Render(Entry entry)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var (name, text, elements) = PlainCards.Draw(entry.Texture, entry.Key.Code, entry.Info, entry.Key.Layout, entry.Key.Fit, entry.Key.Variant, entry.Key.Screen);
            double ms = clock.Elapsed.TotalMilliseconds;
            _spent += ms;
            if (Timings.Count < 8192) Timings.Add(ms);
            if (name != null) Names[entry.Texture] = name;
            if (text != null) Texts[entry.Texture] = text;
            Elements[entry.Texture] = elements;
        }

        /// <summary>Draws queued faces while this frame's budget lasts (the runner, every frame), and frees retired textures.</summary>
        internal static void Drain()
        {
            while (Queue.Count > 0 && Room())
            {
                var entry = Queue.Dequeue();
                if (!entry.Queued || entry.Texture == null) continue;
                entry.Queued = false;
                Render(entry);
            }
            if (Retired.Count > 0 && Time.frameCount % 60 == 0)
            {
                var shown = InUse();
                for (int i = Retired.Count - 1; i >= 0; i--)
                    if (Retired[i] == null || !shown.Contains(Retired[i]))
                    {
                        if (Retired[i] != null) UnityEngine.Object.Destroy(Retired[i]);
                        Retired.RemoveAt(i);
                    }
            }
        }

        /// <summary>Textures a live tagged face shows right now (never evicted from under it).</summary>
        private static HashSet<Texture> InUse()
        {
            var shown = new HashSet<Texture>();
            FaceTag.Live.RemoveWhere(t => t == null);
            float now = Time.realtimeSinceStartup;
            foreach (var held in Held)
                if (held.Value > now && held.Key != null) shown.Add(held.Key);
            foreach (var tag in FaceTag.Live) // (faces on active objects only)
                if (tag != null && tag.Image != null && tag.Image.texture != null) shown.Add(tag.Image.texture);
            return shown;
        }

        /// <summary>Least recently used first, past the cap; a face still on screen stays (the cap is exceeded only when
        /// the faces on screen alone pass it).</summary>
        private static void Evict()
        {
            if (Bytes <= CacheCap) return;
            var shown = InUse();
            // Never the face just asked for (the list's first): it's about to be shown.
            for (var node = Lru.Last; node != null && node != Lru.First && Bytes > CacheCap;)
            {
                var previous = node.Previous;
                var entry = node.Value;
                if (!shown.Contains(entry.Texture))
                {
                    Lru.Remove(node);
                    Cache.Remove(entry.Key);
                    ByTexture.Remove(entry.Texture);
                    Names.Remove(entry.Texture);
                    Texts.Remove(entry.Texture);
                    Elements.Remove(entry.Texture);
                    Bytes -= entry.Bytes;
                    entry.Queued = false;
                    UnityEngine.Object.Destroy(entry.Texture);
                }
                node = previous;
            }
            if (Bytes > CacheCap && !_warnedFull)
            {
                _warnedFull = true;
                Debug.LogWarning($"Plain card cache: the faces on screen alone take {Bytes / (1024 * 1024)} MB (cap {CacheCap / (1024 * 1024)} MB).");
            }
        }

        private static bool OnScreen(RectTransform rt)
        {
            var box = ScreenBox(rt);
            return box.width > 0.5f && box.height > 0.5f && box.xMax > 0 && box.yMax > 0 && box.xMin < Screen.width && box.yMin < Screen.height;
        }

        /// <summary>A rect's bounding box in screen pixels (bottom-left origin, as screenshots are).</summary>
        private static Rect ScreenBox(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera? camera = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var c in corners)
            {
                var p = RectTransformUtility.WorldToScreenPoint(camera, c);
                x0 = Mathf.Min(x0, p.x);
                y0 = Mathf.Min(y0, p.y);
                x1 = Mathf.Max(x1, p.x);
                y1 = Mathf.Max(y1, p.y);
            }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        /// <summary>Drains the queue every frame (after Update, so faces asked for this frame are drawn in later ones).</summary>
        private sealed class Runner : MonoBehaviour
        {
            private static Runner? _instance;

            public static void Ensure()
            {
                if (_instance != null) return;
                var go = new GameObject("PlainCardQueue") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Runner>();
            }

            private void LateUpdate() => Drain();
        }
    }

    public sealed class PlainName { public string Text = ""; public int Size; public Rect Rect; public int Lines; public List<string> Problems = new(); }

    /// <summary>For checks: a plain card's text box as drawn.</summary>
    internal sealed class PlainText { public string Text = ""; public string Pendulum = ""; public bool Italic; public bool AllShown; }

    /// <summary>On every RawImage CardFace.Draw fills (and on CardView faces): what it shows.</summary>
    public sealed class FaceTag : MonoBehaviour
    {
        public uint Code;
        public bool Plain;
        public CardLayout Layout;
        public CardFit Fit;

        /// <summary>The face's image (the cache never evicts a texture an active tagged image holds).</summary>
        internal RawImage? Image;
        /// <summary>What the face was drawn from (to draw a plain face again if it was evicted while hidden).</summary>
        internal CardInfo? Info;
        internal int Width;
        /// <summary>The tags on active objects: their textures stay in the cache.</summary>
        internal static readonly HashSet<FaceTag> Live = new HashSet<FaceTag>();

        private void Awake()
        {
            if (Image == null) Image = GetComponent<RawImage>();
        }

        private void OnEnable()
        {
            Live.Add(this);
            // A plain face evicted while its screen was hidden: drawn again (its texture object was destroyed).
            if (Plain && Image != null && Image.texture == null && Code != 0 && Width > 0)
                Image.texture = CardFace.PlainOnly(Code, Info, Layout, Width, Fit);
        }

        private void OnDisable() => Live.Remove(this);
    }
}
