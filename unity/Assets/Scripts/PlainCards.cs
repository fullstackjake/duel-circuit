using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Interactive;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The plain card's studio (docs/dev/PLAIN-CARDS-SPEC-2026-11-20.md, "How it's drawn"): one hidden world-space canvas
    /// and one disabled orthographic camera on layer 31, a template per layout and fit built once and filled per card,
    /// rendered into a mipmapped render texture and copied into the card's own non-readable texture. Our own layout: the
    /// colour by kind is the card game's colour code; the plates, glyphs, chip, Link grid and patterns are ours.
    /// </summary>
    internal static class PlainCards
    {
        public const int Layer = 31;
        public static readonly Color InkDark = Ui.Hex("#1B1408"), InkLight = Ui.Hex("#F4F1EA");
        public static readonly Color UnknownKind = Ui.Hex("#6B3F1F");
        private static readonly Color Night = Ui.Hex("#10131A");
        private static readonly Color SpellGreen = Ui.Hex("#1D9C73");

        // ---- Colours ----

        public static Color KindColor(CardInfo? info) => info == null ? UnknownKind : CardView.FrameColor(info.Type);
        /// <summary>Ink on the kind colour: CardView.TextColor's rule (light for Xyz, Link, Spell, Trap, Fusion), and unknown cards.</summary>
        public static Color InkOnKind(CardInfo? info) => info == null || CardView.TextColor(info.Type).grayscale > 0.5f ? InkLight : InkDark;
        public static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        /// <summary>The plates (name, chip, text box): dark enough that light ink reads on every kind.</summary>
        public static Color PlateColor(CardInfo? info)
        {
            var k = KindColor(info);
            return Color.Lerp(k, Night, Luma(k) > 0.6f ? 0.68f : 0.5f);
        }
        private static Color RimOf(Color k) => Color.Lerp(k, Color.white, 0.35f);
        private static Color Ground(Color k) => Color.Lerp(k, Night, 0.25f);
        /// <summary>A glyph's ink on a ground: light, or dark on a light ground (LIGHT's yellow disc, a Synchro's window).</summary>
        private static Color GlyphInk(Color ground) => Luma(ground) > 0.6f ? InkDark : InkLight;

        private static readonly Dictionary<uint, (string key, Color color)> Attributes = new Dictionary<uint, (string, Color)>
        {
            [0x01] = ("attr-earth", Ui.Hex("#8A6A3A")), [0x02] = ("attr-water", Ui.Hex("#2E7FD0")), [0x04] = ("attr-fire", Ui.Hex("#D0402E")),
            [0x08] = ("attr-wind", Ui.Hex("#3FA04A")), [0x10] = ("attr-light", Ui.Hex("#D9B52E")), [0x20] = ("attr-dark", Ui.Hex("#7B3FA0")),
            [0x40] = ("attr-divine", Ui.Hex("#C9A227")),
        };

        /// <summary>The card's glyph (attribute, or the Spell / Trap mark) and its disc's colour; none for unknown cards.</summary>
        private static (string? key, Color disc) GlyphOf(CardInfo? info)
        {
            if (info == null) return (null, UnknownKind);
            if ((info.Type & CardType.Spell) != 0) return ("kind-spell", KindColor(info));
            if ((info.Type & CardType.Trap) != 0) return ("kind-trap", KindColor(info));
            return Attributes.TryGetValue(info.Attribute, out var a) ? (a.key, a.color) : ((string?)null, KindColor(info));
        }

        // ---- Layout geometry (card fractions, x from the left, y from the bottom) ----

        public static float RefWidth(CardLayout layout) => layout switch
        {
            CardLayout.Thumb => 40, CardLayout.Compact => 88, CardLayout.Standard => 200, _ => 360,
        };

        private const float HeightOverWidth = 1 / CardFace.Aspect;
        /// <summary>A Compact card's glyph disc centre (x): right of the chip, clear of the hand's Level digit on the left.</summary>
        private const float CompactDiscX = 0.75f;

        private static readonly Vector4 CompactName = new Vector4(0.06f, 0.56f, 0.94f, 0.95f), CompactWindow = new Vector4(0.06f, 0.20f, 0.94f, 0.54f),
            CompactChip = new Vector4(0.06f, 0.29f, 0.58f, 0.45f), Footer = new Vector4(0.04f, 0.03f, 0.96f, 0.18f);
        private static readonly Vector4 BadgedBand = new Vector4(0.40f, 0.78f, 0.94f, 0.97f), BadgedName = new Vector4(0.06f, 0.40f, 0.94f, 0.76f),
            BadgedWindow = new Vector4(0.06f, 0.20f, 0.58f, 0.38f);
        private static readonly Vector4 BigName = new Vector4(0.05f, 0.865f, 0.95f, 0.965f), BigRow = new Vector4(0.05f, 0.80f, 0.95f, 0.86f),
            StandardWindow = new Vector4(0.05f, 0.50f, 0.95f, 0.79f), StandardText = new Vector4(0.06f, 0.20f, 0.94f, 0.49f),
            BigFooter = new Vector4(0.05f, 0.04f, 0.95f, 0.18f), FullWindow = new Vector4(0.05f, 0.66f, 0.95f, 0.79f), FullText = new Vector4(0.06f, 0.20f, 0.94f, 0.65f);
        /// <summary>The info row's glyph disc (0.06 H across, at the row's left) and the chip after it, in card fractions.</summary>
        private static readonly float RowDisc = 0.06f * HeightOverWidth; // the disc's width as a fraction of the card's width
        private static readonly Vector4 RowDiscRect = new Vector4(0.05f, 0.80f, 0.05f + RowDisc, 0.86f);
        private static readonly Vector4 RowChip = new Vector4(0.05f + RowDisc + 0.015f, 0.806f, 0.05f + RowDisc + 0.215f, 0.854f);

        /// <summary>A disc of <paramref name="widthFraction"/> of the card's width centred at (cx, cy), as a card-fraction rect.</summary>
        private static Vector4 Disc(float cx, float cy, float widthFraction)
        {
            float hy = widthFraction * 0.5f / HeightOverWidth;
            return new Vector4(cx - widthFraction * 0.5f, cy - hy, cx + widthFraction * 0.5f, cy + hy);
        }

        private static float BadgedDisc => Mathf.Min(0.30f, 0.16f * HeightOverWidth);

        /// <summary>
        /// The foil shader's regions on a plain card of this layout (UV, origin bottom-left): the name plate, the window, the
        /// glyph disc, the chip, and the text box (Standard, Full) or footer (Compact). A Thumb has none (whole-card foils only).
        /// </summary>
        public static (Vector4 name, Vector4 art, Vector4 attr, Vector4 stars, Vector4 text) FoilRects(CardLayout layout, CardFit fit) => layout switch
        {
            CardLayout.Thumb => (Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero),
            CardLayout.Compact when fit == CardFit.Badged => (BadgedName, BadgedWindow, Disc(0.32f, 0.29f, BadgedDisc), BadgedBand, Footer),
            CardLayout.Compact => (CompactName, CompactWindow, Disc(CompactDiscX, 0.37f, 0.30f), CompactChip, Footer),
            CardLayout.Standard => (BigName, StandardWindow, RowDiscRect, RowChip, StandardText),
            _ => (BigName, FullWindow, RowDiscRect, RowChip, FullText),
        };

        // ---- The studio ----

        private static GameObject? _studio;
        private static Camera _camera = null!;
        private static Canvas _canvas = null!;
        private static RectTransform _canvasRt = null!;
        private static Material _material = null!;
        private static readonly Dictionary<(CardLayout, CardFit), Template> Templates = new Dictionary<(CardLayout, CardFit), Template>();
        private static Template? _active;
        private static bool _warnedCopy;

        /// <summary>For checks: the studio exists once, its camera never renders by itself, its canvas is off between draws,
        /// takes no input, and no other camera sees its layer.</summary>
        public static (bool exists, bool cameraEnabled, bool canvasEnabled, bool raycaster, int camerasSeeingLayer) DebugStudio()
        {
            int seeing = 0;
            foreach (var c in Camera.allCameras)
                if (c != _camera && (c.cullingMask & (1 << Layer)) != 0) seeing++;
            if (_studio == null) return (false, false, false, false, seeing);
            return (true, _camera.enabled, _canvas.enabled, _studio.GetComponentInChildren<GraphicRaycaster>(true) != null, seeing);
        }

        private static void EnsureStudio()
        {
            if (_studio != null) return;
            _studio = new GameObject("PlainCardStudio") { layer = Layer, hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(_studio);
            // Far below the scene, on its own layer, its canvas switched off except inside a draw: nothing else can show it.
            _studio.transform.position = new Vector3(0, -50000, 0);
            var cameraGo = new GameObject("Camera") { layer = Layer };
            cameraGo.transform.SetParent(_studio.transform, false);
            cameraGo.transform.localPosition = new Vector3(0, 0, -10);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 50;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.useOcclusionCulling = false;
            _camera.depth = -100;
            var canvasGo = new GameObject("Canvas", typeof(RectTransform)) { layer = Layer };
            canvasGo.transform.SetParent(_studio.transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.worldCamera = _camera;
            _canvas.enabled = false;
            _canvasRt = (RectTransform)canvasGo.transform;
            _canvasRt.pivot = new Vector2(0.5f, 0.5f);
            _canvasRt.localPosition = Vector3.zero;
            _canvasRt.sizeDelta = new Vector2(88, 128);
            // Every graphic leaves alpha alone (the camera clears it to 1): a see-through pattern or a glyph's soft edge
            // mustn't make the face see-through.
            _material = new Material(Canvas.GetDefaultCanvasMaterial()) { name = "Plain card studio" };
            _material.SetFloat("_ColorMask", 14); // RGB, no A
            foreach (var c in Camera.allCameras)
                if (c != _camera) c.cullingMask &= ~(1 << Layer);
            Application.quitting += Shutdown;
        }

        private static void Shutdown()
        {
            if (_studio != null) UnityEngine.Object.Destroy(_studio);
            _studio = null;
            Templates.Clear();
            _active = null;
        }

        private static Template TemplateFor(CardLayout layout, CardFit fit)
        {
            if (Templates.TryGetValue((layout, fit), out var t)) return t;
            float w = RefWidth(layout);
            _canvasRt.sizeDelta = new Vector2(w, w * HeightOverWidth);
            t = new Template(_canvasRt, layout, fit, w, w * HeightOverWidth);
            foreach (var part in t.Root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = Layer;
            foreach (var g in t.Root.GetComponentsInChildren<Graphic>(true))
            {
                g.material = _material;
                g.raycastTarget = false;
            }
            t.Root.gameObject.SetActive(false);
            return Templates[(layout, fit)] = t;
        }

        public static int MipsFor(int w, int h) => 1 + (int)Math.Floor(Math.Log(Math.Max(w, h), 2));

        /// <summary>A plain card's own texture: mipmapped, not readable (GPU only), trilinear.</summary>
        public static Texture2D NewTexture(int width, int height, string name)
        {
            var texture = new Texture2D(width, height, GraphicsFormat.R8G8B8A8_UNorm, MipsFor(width, height),
                TextureCreationFlags.MipChain | TextureCreationFlags.DontInitializePixels)
            {
                name = name, filterMode = FilterMode.Trilinear, anisoLevel = 2, wrapMode = TextureWrapMode.Clamp,
            };
            texture.Apply(false, true);
            return texture;
        }

        public static long BytesOf(Texture texture)
        {
            long bytes = 0;
            for (int i = 0, w = texture.width, h = texture.height; i < MipsFor(texture.width, texture.height); i++, w = Math.Max(1, w / 2), h = Math.Max(1, h / 2))
                bytes += (long)w * h * 4;
            return bytes;
        }

        /// <summary>Draws the card into <paramref name="target"/>; returns its name and text as laid out (for checks).</summary>
        public static (PlainName? name, PlainText? text) Draw(Texture2D target, uint code, CardInfo? info, CardLayout layout, CardFit fit)
        {
            EnsureStudio();
            var t = TemplateFor(layout, fit);
            int w = target.width, h = target.height;
            float refW = RefWidth(layout), refH = refW * h / w;
            if (_active != t)
            {
                if (_active != null) _active.Root.gameObject.SetActive(false);
                t.Root.gameObject.SetActive(true);
                _active = t;
            }
            _canvasRt.sizeDelta = new Vector2(refW, refH);
            // Text is drawn at the texture's own resolution (a world canvas's text is rasterised at its scale factor).
            _canvas.scaleFactor = w / refW;
            var text = t.Fill(code, info, _canvas.scaleFactor);
            _canvas.enabled = true;
            // Every character every text on the face may be laid out at, asked for up front: the shared font atlas is
            // rebuilt (if it must be) now, not while the face is laid out.
            Watch();
            RequestAll(t.Root, _canvas.scaleFactor);
            _drawing = true;
            // Lays out the studio's own texts and images now (else it would render the last card's layout); never the
            // game's other canvases (Canvas.ForceUpdateCanvases would rebuild them all, and could run game code).
            foreach (var g in t.Root.GetComponentsInChildren<Graphic>(false)) g.Rebuild(CanvasUpdate.PreRender);
            var name = t.NameAsDrawn(_canvasRt, _canvas.scaleFactor);
            if (text != null) text.AllShown = t.TextAllShown();

            var desc = new RenderTextureDescriptor(w, h, GraphicsFormat.R8G8B8A8_UNorm, 0)
            {
                useMipMap = true, autoGenerateMips = false, mipCount = target.mipmapCount, msaaSamples = 1, sRGB = false,
            };
            var rt = RenderTexture.GetTemporary(desc);
            _camera.targetTexture = rt;
            _camera.orthographicSize = refH / 2;
            _camera.aspect = (float)w / h;
            _camera.backgroundColor = KindColor(info);
            _camera.Render();
            _drawing = false;
            _camera.targetTexture = null;
            _canvas.enabled = false;
            rt.GenerateMips();
            Copy(rt, target);
            RenderTexture.ReleaseTemporary(rt);
            return (name, text);
        }

        private static bool _drawing, _watching;

        /// <summary>For checks: how often the shared font atlas was rebuilt while a face was being laid out or rendered.</summary>
        internal static int DebugMidDrawRebuilds;

        private static void Watch()
        {
            if (_watching) return;
            _watching = true;
            Font.textureRebuilt += _ => { if (_drawing) DebugMidDrawRebuilds++; };
        }

        /// <summary>For checks: makes the font atlas rebuild as if mid-draw (many new characters at big sizes), to show the
        /// count sees it.</summary>
        internal static void DebugRebuildMidDraw()
        {
            Watch();
            var chars = new System.Text.StringBuilder();
            for (char c = '!'; c <= '~'; c++) chars.Append(c);
            for (char c = '\u00C0'; c <= '\u00FF'; c++) chars.Append(c);
            _drawing = true;
            for (int px = 150; px <= 190; px += 4) Ui.Font.RequestCharactersInTexture(chars.ToString(), px, FontStyle.Bold);
            _drawing = false;
        }

        /// <summary>Asks the font for every text's characters at every pixel size it may be laid out at (best fit's range).</summary>
        private static void RequestAll(Transform root, float scale)
        {
            foreach (var text in root.GetComponentsInChildren<Text>(false))
            {
                if (string.IsNullOrEmpty(text.text)) continue;
                var font = text.font != null ? text.font : Ui.Font;
                int lo = text.resizeTextForBestFit ? Mathf.Min(text.resizeTextMinSize, text.fontSize) : text.fontSize;
                int hi = text.resizeTextForBestFit ? Mathf.Max(text.resizeTextMaxSize, text.fontSize) : text.fontSize;
                for (int px = Mathf.Max(1, Mathf.FloorToInt(lo * scale)); px <= Mathf.CeilToInt(hi * scale); px++)
                    font.RequestCharactersInTexture(text.text, px, text.fontStyle);
            }
        }

        private static void Copy(Texture source, Texture2D target)
        {
            if (source.mipmapCount == target.mipmapCount && (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.RTToTexture) != 0)
            {
                Graphics.CopyTexture(source, target);
                return;
            }
            if (!_warnedCopy)
            {
                _warnedCopy = true;
                Debug.LogWarning($"Plain cards: can't copy the studio's render ({SystemInfo.copyTextureSupport}, mips {source.mipmapCount} / {target.mipmapCount}); copying what matches.");
            }
            for (int m = 0; m < Math.Min(source.mipmapCount, target.mipmapCount); m++) Graphics.CopyTexture(source, 0, m, target, 0, m);
        }

        // ---- Blanks: the kind's colour and rim, for faces waiting in the queue ----

        private static readonly Dictionary<(Color32, bool, int, int), Texture2D> Blanks = new Dictionary<(Color32, bool, int, int), Texture2D>();

        /// <summary>Fills <paramref name="target"/> with its kind's blank (K and the rim, drawn once per kind and size).</summary>
        public static void Blank(Texture2D target, CardInfo? info, CardLayout layout, CardFit fit)
        {
            var k = KindColor(info);
            bool pendulum = IsPendulum(info);
            int w = target.width, h = target.height;
            if (!Blanks.TryGetValue((k, pendulum, w, h), out var blank))
            {
                blank = new Texture2D(w, h, GraphicsFormat.R8G8B8A8_UNorm, target.mipmapCount, TextureCreationFlags.MipChain) { name = "plain blank" };
                var px = new Color32[w * h];
                float inset = 0.03f * w, line = 0.02f * w;
                for (int y = 0; y < h; y++)
                {
                    float fy = (y + 0.5f) / h;
                    var bottom = pendulum && fy < 0.4f ? Color.Lerp(SpellGreen, k, fy / 0.4f) : k;
                    Color32 fill = bottom, rim = RimOf(bottom);
                    for (int x = 0; x < w; x++)
                    {
                        float dx = Mathf.Min(x + 0.5f, w - x - 0.5f), dy = Mathf.Min(y + 0.5f, h - y - 0.5f), d = Mathf.Min(dx, dy);
                        px[y * w + x] = d >= inset && d < inset + line ? rim : fill;
                    }
                }
                blank.SetPixels32(px);
                blank.Apply(true, true);
                Blanks[(k, pendulum, w, h)] = blank;
            }
            if (blank.mipmapCount == target.mipmapCount) Graphics.CopyTexture(blank, target);
        }

        private static bool IsPendulum(CardInfo? info) => info != null && (info.Type & CardType.Monster) != 0 && (info.Type & CardType.Pendulum) != 0;

        // ---- Patterns: six small tiles drawn in code, picked by the card's code ----

        private static readonly Texture2D?[] Patterns = new Texture2D?[6];

        private static Texture2D PatternFor(int index)
        {
            if (Patterns[index] is Texture2D cached) return cached;
            const int n = 64, ss = 4;
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = $"plain pattern {index}", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                            sum += Inside(index, (x + (sx + 0.5f) / ss) / n, (y + (sy + 0.5f) / ss) / n) ? 1 : 0;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(sum / (ss * ss) * 255));
                }
            texture.SetPixels32(px);
            texture.Apply(true, true);
            return (Patterns[index] = texture)!;
        }

        /// <summary>Whether a tile point (u, v in 0..1) is on the pattern's ink.</summary>
        private static bool Inside(int pattern, float u, float v)
        {
            static float Frac(float f) => f - Mathf.Floor(f);
            switch (pattern)
            {
                case 0: // concentric rings
                {
                    float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f));
                    return Frac(d * 7f) < 0.38f;
                }
                case 1: return Frac((u + v) * 2) < 0.42f; // diagonal bands
                case 2: return Frac(v * 2 + Mathf.Abs(u - 0.5f) * 2) < 0.36f; // nested chevrons
                case 3: // a dot grid
                {
                    float du = Frac(u * 2) - 0.5f, dv = Frac(v * 2) - 0.5f;
                    return du * du + dv * dv < 0.2f * 0.2f;
                }
                case 4: return Frac(Mathf.Atan2(v, u) / (Mathf.PI / 2) * 9) < 0.45f; // rays from a corner
                default: return Frac((v + 0.07f * Mathf.Sin(u * Mathf.PI * 2)) * 3) < 0.34f; // horizontal waves
            }
        }

        // ---- Words ----

        private static string Stat(int value) => CardDetail.Stat(value);

        /// <summary>A Spell's or Trap's subtype on its chip, or null for a Normal one.</summary>
        private static string? SubtypeChip(uint type) =>
            (type & 0x10000) != 0 ? "QUICK" : (type & 0x20000) != 0 ? "CONT" : (type & 0x40000) != 0 ? "EQUIP"
            : (type & 0x80000) != 0 ? "FIELD" : (type & 0x80) != 0 ? "RITUAL" : (type & 0x100000) != 0 ? "COUNTER" : null;

        /// <summary>The type line: CardDetail's words without the brackets ("Dragon · Tuner · Effect", "Quick-Play Spell").</summary>
        private static string TypeLine(CardInfo info)
        {
            if ((info.Type & CardType.Monster) == 0)
            {
                bool spell = (info.Type & CardType.Spell) != 0;
                uint type = info.Type;
                string kind = (type & 0x10000) != 0 ? "Quick-Play" : (type & 0x20000) != 0 ? "Continuous" : (type & 0x40000) != 0 ? "Equip"
                    : (type & 0x80000) != 0 ? "Field" : (type & 0x80) != 0 ? "Ritual" : (type & 0x100000) != 0 ? "Counter" : "Normal";
                return $"{kind} {(spell ? "Spell" : "Trap")}";
            }
            var parts = new List<string>();
            foreach (var (bit, name) in DuelText.Races)
                if ((info.Race & bit) != 0) { parts.Add(name); break; }
            foreach (var (bit, name) in new (uint, string)[]
                     {
                         (0x40, "Fusion"), (0x80, "Ritual"), (0x2000, "Synchro"), (0x800000, "Xyz"), (0x1000000, "Pendulum"), (0x4000000, "Link"),
                         (0x200000, "Flip"), (0x400000, "Toon"), (0x200, "Spirit"), (0x400, "Union"), (0x800, "Gemini"), (0x1000, "Tuner"),
                     })
                if ((info.Type & bit) != 0) parts.Add(name);
            if ((info.Type & 0x20) != 0) parts.Add("Effect");
            else if ((info.Type & 0x10) != 0) parts.Add("Normal");
            return string.Join(" · ", parts);
        }

        private static readonly Regex Rule = new Regex(@"\n-{5,}\n", RegexOptions.Compiled);
        private static readonly Regex Header = new Regex(@"^\[ ?[A-Za-z ]+ ?\]\s*\n?", RegexOptions.Compiled);

        /// <summary>The card's text, and a Pendulum's Pendulum effect apart (headers like "[ Monster Effect ]" left out).</summary>
        private static (string text, string pendulum) TextsOf(CardInfo info)
        {
            var text = (info.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (!IsPendulum(info)) return (text, "");
            var parts = Rule.Split(text, 2);
            if (parts.Length < 2) return (Header.Replace(text, "").Trim(), "");
            return (Header.Replace(parts[1].Trim(), "").Trim(), Header.Replace(parts[0].Trim(), "").Trim());
        }

        /// <summary>
        /// <paramref name="s"/> as it fits <paramref name="height"/> at <paramref name="size"/>: whole, or cut after the last
        /// word that fits with "…" (the detail panel has the whole text).
        /// </summary>
        private static string Cut(Text t, string s, int size, float width, float height)
        {
            if (s.Length == 0 || CardView.TextHeight(t, s, size, width) <= height) return s;
            var words = s.Split(' ');
            int lo = 0, hi = words.Length - 1;
            string Joined(int count) => string.Join(" ", words, 0, count).TrimEnd(' ', ',', ';', ':', '\n') + "…";
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (CardView.TextHeight(t, Joined(mid), size, width) <= height) lo = mid;
                else hi = mid - 1;
            }
            return Joined(lo);
        }

        // ---- A template: one per layout and fit, filled per card ----

        private sealed class Template
        {
            public readonly RectTransform Root;
            private readonly CardLayout _layout;
            private readonly CardFit _fit;
            private readonly float _w, _h;
            private readonly PlainGradient _base;
            private readonly PlainGradient[] _rim = new PlainGradient[4];
            private readonly Image? _namePlate, _chip, _textBox, _rule, _window, _infoChip;
            private readonly Text? _name, _chipText, _footer, _body, _pendBody, _infoChipText, _typeLine, _rating;
            private readonly RawImage? _pattern, _disc, _glyph, _infoDisc, _infoGlyph;
            private readonly RectTransform? _grid, _bodyRt, _pendRt, _ruleRt;
            private readonly Image[] _cells = new Image[9];
            private Vector4 _windowRect;
            private int _nameMax, _nameMin, _bodyMax, _bodyMin;

            public Template(RectTransform canvas, CardLayout layout, CardFit fit, float w, float h)
            {
                _layout = layout;
                _fit = fit;
                _w = w;
                _h = h;
                Root = Ui.Stretched($"{layout} {fit}", canvas, Vector2.zero, Vector2.one);
                _base = Gradient("Base", Root, new Vector4(0, 0, 1, 1));
                // The rim: a line 0.02 W wide, inset 0.03 W.
                float inset = 0.03f * w, line = 0.02f * w;
                _rim[0] = Edge("Top", new Vector2(0, 1), Vector2.one, new Vector2(inset, -inset - line), new Vector2(-inset, -inset));
                _rim[1] = Edge("Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(inset, inset), new Vector2(-inset, inset + line));
                _rim[2] = Edge("Left", Vector2.zero, new Vector2(0, 1), new Vector2(inset, inset), new Vector2(inset + line, -inset));
                _rim[3] = Edge("Right", new Vector2(1, 0), Vector2.one, new Vector2(-inset - line, inset), new Vector2(-inset, -inset));

                if (layout == CardLayout.Thumb)
                {
                    // An unknown card's Thumb shows its pattern where the glyph would be (else it would read as blank).
                    (_window, _pattern) = WindowAt(Disc(0.5f, 0.5f, 0.6f));
                    _disc = Raw("Disc", Disc(0.5f, 0.5f, 0.6f), Icons.Round);
                    _glyph = Raw("Glyph", Disc(0.5f, 0.5f, 0.42f), null);
                    return;
                }
                if (layout == CardLayout.Compact)
                {
                    bool badged = fit == CardFit.Badged;
                    (_namePlate, _name) = Plate("Name", badged ? BadgedName : CompactName, TextAnchor.MiddleCenter);
                    _nameMax = 14;
                    _nameMin = 7;
                    _windowRect = badged ? BadgedWindow : CompactWindow;
                    (_window, _pattern) = WindowAt(_windowRect);
                    // The glyph at the right, the chip at the left: in the hand the big Level digit sits on the left edge (BoardView.HandLevel).
                    var disc = badged ? Disc(0.32f, 0.29f, BadgedDisc) : Disc(CompactDiscX, 0.37f, 0.30f);
                    _disc = Raw("Disc", disc, Icons.Round);
                    _glyph = Raw("Glyph", Shrunk(disc, 0.72f), null);
                    var chip = badged ? new Vector4(0.42f, 0.80f, 0.94f, 0.95f) : CompactChip;
                    (_chip, _chipText) = Plate("Chip", chip, TextAnchor.MiddleCenter, chip: true);
                    _chipText!.fontStyle = FontStyle.Bold;
                    Best(_chipText, 6, 12);
                    _grid = Grid(badged ? Disc(0.83f, 0.875f, 0.22f) : Disc(0.30f, 0.37f, 0.30f), out _rating);
                    _footer = Words("Footer", Footer, 12, TextAnchor.MiddleCenter);
                    _footer.fontStyle = FontStyle.Bold;
                    Best(_footer, 7, 12);
                    return;
                }
                bool full = layout == CardLayout.Full;
                (_namePlate, _name) = Plate("Name", BigName, TextAnchor.MiddleLeft);
                // (The spec's 13 at Standard can't fit two lines in its 0.1 H plate: 11 does, so a long name isn't cut.)
                _nameMax = full ? 30 : 20;
                _nameMin = full ? 18 : 11;
                _infoDisc = Raw("InfoDisc", RowDiscRect, Icons.Round);
                _infoGlyph = Raw("InfoGlyph", Shrunk(RowDiscRect, 0.72f), null);
                (_infoChip, _infoChipText) = Plate("InfoChip", RowChip, TextAnchor.MiddleCenter, chip: true);
                _infoChipText!.fontStyle = FontStyle.Bold;
                Best(_infoChipText, 6, full ? 15 : 11);
                _typeLine = Words("TypeLine", new Vector4(RowChip.z + 0.02f, BigRow.y, BigRow.z, BigRow.w), full ? 15 : 11, TextAnchor.MiddleRight);
                Best(_typeLine, 6, full ? 15 : 11);
                _windowRect = full ? FullWindow : StandardWindow;
                (_window, _pattern) = WindowAt(_windowRect);
                float windowHeight = (_windowRect.w - _windowRect.y) * HeightOverWidth; // in card widths
                float glyph = windowHeight * 0.5f;
                _glyph = Raw("Glyph", full ? Disc(_windowRect.x + 0.03f + glyph / 2, (_windowRect.y + _windowRect.w) / 2, glyph)
                    : Disc(0.5f, (_windowRect.y + _windowRect.w) / 2, glyph), null);
                var box = full ? FullText : StandardText;
                _textBox = Ui.Image(Frac("TextBox", Root, box), Color.black);
                float pad = 0.025f * w;
                _pendRt = Ui.Stretched("Pendulum", _textBox.rectTransform, Vector2.zero, Vector2.one);
                _pendBody = TextIn(_pendRt, full ? 15 : 11, TextAnchor.UpperLeft);
                _ruleRt = Ui.Stretched("Rule", _textBox.rectTransform, Vector2.zero, Vector2.one);
                _rule = Ui.Image(_ruleRt, Color.white);
                _bodyRt = Ui.Stretched("Body", _textBox.rectTransform, Vector2.zero, Vector2.one);
                _bodyRt.offsetMin = new Vector2(pad, pad * 0.6f);
                _bodyRt.offsetMax = new Vector2(-pad, -pad * 0.6f);
                _body = TextIn(_bodyRt, full ? 15 : 11, TextAnchor.UpperLeft);
                _bodyMax = full ? 15 : 11;
                _bodyMin = full ? 11 : 8;
                _footer = Words("Footer", BigFooter, full ? 18 : 13, TextAnchor.MiddleLeft);
                _footer.fontStyle = FontStyle.Bold;
                Best(_footer, 8, full ? 18 : 13);
                var footerGrid = Disc(BigFooter.z - 0.08f, (BigFooter.y + BigFooter.w) / 2, 0.16f);
                _grid = Grid(footerGrid, out _rating);
            }

            private RectTransform Frac(string name, Transform parent, Vector4 r) => Ui.Stretched(name, parent, new Vector2(r.x, r.y), new Vector2(r.z, r.w));

            private static Vector4 Shrunk(Vector4 r, float k)
            {
                float cx = (r.x + r.z) / 2, cy = (r.y + r.w) / 2;
                return new Vector4(cx - (r.z - r.x) * k / 2, cy - (r.w - r.y) * k / 2, cx + (r.z - r.x) * k / 2, cy + (r.w - r.y) * k / 2);
            }

            private PlainGradient Gradient(string name, Transform parent, Vector4 r) => Frac(name, parent, r).gameObject.AddComponent<PlainGradient>();

            private PlainGradient Edge(string name, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
            {
                var rt = Ui.Stretched(name, Root, min, max);
                rt.offsetMin = offsetMin;
                rt.offsetMax = offsetMax;
                return rt.gameObject.AddComponent<PlainGradient>();
            }

            private RawImage Raw(string name, Vector4 r, Texture? texture)
            {
                var raw = Frac(name, Root, r).gameObject.AddComponent<RawImage>();
                raw.texture = texture;
                return raw;
            }

            private (Image, RawImage) WindowAt(Vector4 r)
            {
                var ground = Ui.Image(Frac("Window", Root, r), Color.black);
                var pattern = Ui.Stretched("Pattern", ground.rectTransform, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
                return (ground, pattern);
            }

            /// <summary>A plate with its text: names padded 0.03 W at the sides, chips hardly at all (their words are short).</summary>
            private (Image, Text) Plate(string name, Vector4 r, TextAnchor anchor, bool chip = false)
            {
                var plate = Ui.Image(Frac(name, Root, r), Color.black);
                var textRt = Ui.Stretched("Text", plate.rectTransform, Vector2.zero, Vector2.one);
                float padX = (chip ? 0.012f : 0.03f) * _w, padY = (chip ? 0.002f : 0.005f) * _h;
                textRt.offsetMin = new Vector2(padX, padY);
                textRt.offsetMax = new Vector2(-padX, -padY);
                return (plate, TextIn(textRt, 12, anchor));
            }

            private Text Words(string name, Vector4 r, int size, TextAnchor anchor) => TextIn(Frac(name, Root, r), size, anchor);

            private static Text TextIn(RectTransform rt, int size, TextAnchor anchor)
            {
                var t = rt.gameObject.AddComponent<Text>();
                t.font = Ui.Font;
                t.fontSize = size;
                t.alignment = anchor;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.supportRichText = false;
                t.raycastTarget = false;
                return t;
            }

            private static void Best(Text t, int min, int max)
            {
                t.fontSize = max;
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = min;
                t.resizeTextMaxSize = max;
            }

            /// <summary>A 3x3 Link grid: the eight outer cells (Link Markers), the rating in the centre.</summary>
            private RectTransform Grid(Vector4 r, out Text? rating)
            {
                var grid = Frac("LinkGrid", Root, r);
                const float gap = 0.06f;
                for (int i = 0; i < 9; i++)
                {
                    int col = i % 3, row = i / 3; // row 0 at the top
                    float x0 = col / 3f + gap / 2, x1 = (col + 1) / 3f - gap / 2, y1 = 1 - row / 3f - gap / 2, y0 = 1 - (row + 1) / 3f + gap / 2;
                    _cells[i] = Ui.Image(Ui.Stretched($"Cell{i}", grid, new Vector2(x0, y0), new Vector2(x1, y1)), Color.black);
                }
                rating = TextIn(Ui.Stretched("Rating", _cells[4].rectTransform, Vector2.zero, Vector2.one), 12, TextAnchor.MiddleCenter);
                rating.fontStyle = FontStyle.Bold;
                Best(rating, 5, 14);
                return grid;
            }

            /// <summary>Link Marker bits by grid cell (row by row from the top left; the centre has none).</summary>
            private static readonly uint[] Markers = { 0x40, 0x80, 0x100, 0x08, 0, 0x20, 0x01, 0x02, 0x04 };

            private static void Show(Component? c, bool on)
            {
                if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
            }

            /// <summary>Fills the template for one card; returns its text box as laid out (Standard, Full).</summary>
            public PlainText? Fill(uint code, CardInfo? info, float scale)
            {
                var k = KindColor(info);
                bool known = info != null, pendulum = IsPendulum(info);
                bool monster = known && (info!.Type & CardType.Monster) != 0, link = monster && (info!.Type & CardType.Link) != 0;
                var bottom = pendulum ? SpellGreen : k;
                _base.Set(k, bottom, 0.4f);
                _rim[0].Set(RimOf(k), RimOf(k), 0);
                _rim[1].Set(RimOf(bottom), RimOf(bottom), 0);
                float inset = 0.03f * _w, split = (0.4f * _h - inset) / (_h - 2 * inset);
                _rim[2].Set(RimOf(k), RimOf(bottom), split);
                _rim[3].Set(RimOf(k), RimOf(bottom), split);
                var plate = PlateColor(info);
                var ink = InkOnKind(info);
                var (glyphKey, discColor) = GlyphOf(info);
                var glyphTexture = glyphKey != null ? Icons.For(glyphKey) : null;

                // The window's pattern, by code: tiles about a fifth of the card wide (rings and rays one across it).
                if (_window != null && _pattern != null)
                {
                    // An unknown card's Thumb has nothing else on it: its pattern in light ink on a plate, so it reads.
                    bool thumbOnly = _layout == CardLayout.Thumb;
                    _window.color = thumbOnly ? plate : Ground(k);
                    int index = (int)(code % 6);
                    _pattern.texture = PatternFor(index);
                    _pattern.color = thumbOnly ? InkLight.WithAlpha(0.6f) : Color.Lerp(k, Color.white, 0.2f).WithAlpha(0.5f);
                    var size = _window.rectTransform.rect.size;
                    if (size.x <= 0) size = new Vector2(_w * 0.6f, _w * 0.6f);
                    _pattern.uvRect = index is 0 or 4 ? new Rect(0, 0, 1, size.y / size.x) : new Rect(0, 0, size.x / (0.22f * _w), size.y / (0.22f * _w));
                }

                if (_layout == CardLayout.Thumb)
                {
                    // A Spell's or Trap's disc is its own kind colour, so on a Thumb it vanished into the base and left only the
                    // glyph's thin strokes: those (and unknown cards) show their pattern on a plate under the glyph instead.
                    bool onPlate = !known || ((Color32)discColor).Equals((Color32)k);
                    Show(_window, onPlate);
                    Show(_disc, glyphTexture != null && !onPlate);
                    Show(_glyph, glyphTexture != null);
                    if (_disc != null) _disc.color = discColor;
                    if (_glyph != null) { _glyph.texture = glyphTexture; _glyph.color = GlyphInk(onPlate ? plate : discColor); }
                    return null;
                }

                // The name plate: light ink on its plate, words whole (CardView.FitWords), then best fit.
                _namePlate!.color = plate;
                _name!.color = InkLight;
                _name.fontStyle = FontStyle.Normal;
                _name.text = info?.Name ?? $"Card {code}";
                var nameRt = _name.rectTransform;
                float nameWidth = _namePlate.rectTransform.rect.width + nameRt.offsetMax.x - nameRt.offsetMin.x;
                _name.resizeTextForBestFit = false;
                CardView.FitWords(_name, nameWidth, _nameMax);
                _name.resizeTextMinSize = Mathf.Min(_name.fontSize, _nameMin);
                _name.horizontalOverflow = HorizontalWrapMode.Wrap;
                Ui.Font.RequestCharactersInTexture(_name.text, Mathf.Max(1, Mathf.RoundToInt(_name.fontSize * scale)), FontStyle.Normal);

                string? chipText = null;
                if (monster)
                {
                    uint level = info!.Level & 0xff;
                    chipText = link ? $"LINK {level}" : (info.Type & CardType.Xyz) != 0 ? $"RK {level}" : $"LV {level}";
                }
                else if (known) chipText = SubtypeChip(info!.Type);

                if (_layout == CardLayout.Compact)
                {
                    Show(_disc, glyphTexture != null);
                    Show(_glyph, glyphTexture != null);
                    if (_disc != null) _disc.color = discColor;
                    if (_glyph != null) { _glyph.texture = glyphTexture; _glyph.color = GlyphInk(discColor); }
                    // The chip (or the Link grid in its place).
                    Show(_chip, chipText != null && !link);
                    _chip!.color = plate;
                    _chipText!.color = InkLight;
                    _chipText.text = chipText ?? "";
                    FillGrid(link ? info : null, plate);
                    _footer!.color = ink;
                    _footer.text = !known ? "" : !monster ? ((info!.Type & CardType.Spell) != 0 ? "SPELL" : "TRAP")
                        : link ? $"ATK {Stat(info!.Attack)}" : $"{Stat(info!.Attack)}/{Stat(info.Defense)}";
                    return null;
                }

                // Standard and Full: the info row, the window's glyph, the text box, the footer.
                Show(_infoDisc, glyphTexture != null);
                Show(_infoGlyph, glyphTexture != null);
                _infoDisc!.color = discColor;
                _infoGlyph!.texture = glyphTexture;
                _infoGlyph.color = GlyphInk(discColor);
                Show(_infoChip, chipText != null);
                _infoChip!.color = plate;
                _infoChipText!.color = InkLight;
                _infoChipText.text = chipText ?? "";
                _typeLine!.color = ink;
                _typeLine.text = known ? TypeLine(info!) : "";
                Show(_glyph, glyphTexture != null);
                _glyph!.texture = glyphTexture;
                _glyph.color = GlyphInk(Ground(k)).WithAlpha(0.9f);

                Show(_textBox, known);
                FillGrid(link ? info : null, plate);
                _footer!.color = ink;
                if (!known) _footer.text = "";
                else if (!monster) _footer.text = (info!.Type & CardType.Spell) != 0 ? "SPELL" : "TRAP";
                else if (link) _footer.text = $"ATK {Stat(info!.Attack)}";
                else _footer.text = $"ATK {Stat(info!.Attack)}   DEF {Stat(info.Defense)}" + (pendulum ? $"\nSCALE {info.LScale} / {info.RScale}" : "");
                if (!known) return null;
                return FillText(info!, plate);
            }

            private void FillGrid(CardInfo? link, Color plate)
            {
                Show(_grid, link != null);
                if (link == null) return;
                for (int i = 0; i < 9; i++)
                    _cells[i].color = i != 4 && (link.LinkMarker & Markers[i]) != 0 ? InkLight : plate; // lit where it has a Marker, dim (plate) where not
                _rating!.color = InkLight;
                _rating.text = (link.Level & 0xff).ToString();
            }

            private PlainText FillText(CardInfo info, Color plate)
            {
                _textBox!.color = plate;
                var (text, pendulumText) = TextsOf(info);
                bool italic = (info.Type & CardType.Normal) != 0 && (info.Type & CardType.Effect) == 0;
                var boxRt = _textBox.rectTransform;
                var box = boxRt.rect.size;
                float pad = 0.025f * _w, innerW = box.x - 2 * pad, innerH = box.y - 1.2f * pad;
                const float margin = 0.92f; // what the layout measure and the texture's pixel sizes may disagree by
                _body!.fontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
                _body.color = InkLight;
                Best(_body, _bodyMin, _bodyMax);
                _pendBody!.color = InkLight;
                _pendBody.fontStyle = FontStyle.Normal;
                Best(_pendBody, _bodyMin, _bodyMax);
                bool split = pendulumText.Length > 0;
                Show(_pendBody, split);
                Show(_rule, split);
                if (!split)
                {
                    _bodyRt!.anchorMin = Vector2.zero;
                    _bodyRt.anchorMax = Vector2.one;
                    _body.text = Cut(_body, text, _bodyMin, innerW, innerH * margin);
                }
                else
                {
                    // The Pendulum effect first, a thin rule, then the monster's text: the box shared by their lengths.
                    float hp = CardView.TextHeight(_pendBody, pendulumText, _bodyMin, innerW), hm = CardView.TextHeight(_body, text, _bodyMin, innerW);
                    float share = Mathf.Clamp(hp / Mathf.Max(1, hp + hm), 0.3f, 0.6f);
                    _pendRt!.anchorMin = new Vector2(0, 1 - share);
                    _pendRt.anchorMax = Vector2.one;
                    _pendRt.offsetMin = new Vector2(pad, 0.004f * _h);
                    _pendRt.offsetMax = new Vector2(-pad, -pad * 0.6f);
                    _ruleRt!.anchorMin = new Vector2(0, 1 - share);
                    _ruleRt.anchorMax = new Vector2(1, 1 - share);
                    _ruleRt.offsetMin = new Vector2(pad, -0.5f);
                    _ruleRt.offsetMax = new Vector2(-pad, 0.5f);
                    _rule!.color = InkLight.WithAlpha(0.45f);
                    _bodyRt!.anchorMin = Vector2.zero;
                    _bodyRt.anchorMax = new Vector2(1, 1 - share);
                    _bodyRt.offsetMin = new Vector2(pad, pad * 0.6f);
                    _bodyRt.offsetMax = new Vector2(-pad, -0.004f * _h);
                    float pendH = share * box.y - 0.6f * pad - 0.004f * _h, bodyH = (1 - share) * box.y - 0.6f * pad - 0.004f * _h;
                    _pendBody.text = Cut(_pendBody, pendulumText, _bodyMin, innerW, pendH * margin);
                    _body.text = Cut(_body, text, _bodyMin, innerW, bodyH * margin);
                }
                return new PlainText { Text = _body.text, Pendulum = split ? _pendBody.text : "", Italic = italic };
            }

            /// <summary>Whether the text box shows every character it was given (read after the canvas update).</summary>
            public bool TextAllShown()
            {
                bool Shown(Text? t) => t == null || !t.gameObject.activeInHierarchy || t.text.Length == 0
                                       || t.cachedTextGenerator.characterCountVisible >= t.text.Replace("\n", "").Length - 1;
                return Shown(_body) && Shown(_pendBody);
            }

            /// <summary>The name as drawn (read after the canvas update): its text, size, rect in card fractions, problems.</summary>
            public PlainName? NameAsDrawn(RectTransform canvas, float scale)
            {
                if (_name == null) return null;
                var gen = _name.cachedTextGenerator;
                var corners = new Vector3[4];
                var card = new Vector3[4];
                _name.rectTransform.GetWorldCorners(corners);
                canvas.GetWorldCorners(card);
                float cw = card[2].x - card[0].x, ch = card[2].y - card[0].y;
                var problems = new List<string>(CardView.BrokenWords(_name));
                problems.AddRange(CardView.SplitDigraphs(_name));
                return new PlainName
                {
                    Text = _name.text,
                    Size = gen.fontSizeUsedForBestFit > 0 ? Mathf.RoundToInt(gen.fontSizeUsedForBestFit / scale) : _name.fontSize,
                    Rect = Rect.MinMaxRect((corners[0].x - card[0].x) / cw, (corners[0].y - card[0].y) / ch, (corners[2].x - card[0].x) / cw, (corners[2].y - card[0].y) / ch),
                    Problems = problems,
                };
            }
        }
    }

    /// <summary>A rect filled with one colour, or one running into another over its lower part (a Pendulum's base and rim).</summary>
    internal sealed class PlainGradient : MaskableGraphic
    {
        private Color _top = Color.white, _bottom = Color.white;
        private float _split;

        public void Set(Color top, Color bottom, float split)
        {
            if (top == _top && bottom == _bottom && Mathf.Approximately(split, _split)) return;
            _top = top;
            _bottom = bottom;
            _split = split;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            if (_top == _bottom) { Quad(vh, r.xMin, r.yMin, r.xMax, r.yMax, _top, _top); return; }
            float ys = Mathf.Lerp(r.yMin, r.yMax, Mathf.Clamp01(_split));
            Quad(vh, r.xMin, ys, r.xMax, r.yMax, _top, _top);
            Quad(vh, r.xMin, r.yMin, r.xMax, ys, _bottom, _top);
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color bottom, Color top)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), bottom, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), top, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), top, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), bottom, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }
}
