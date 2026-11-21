using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-plaincheck &lt;folder&gt;` (absolute; with -mods &lt;empty folder&gt; -pics &lt;empty folder&gt; -hdpics off): the plain
    /// card's renderer (docs/dev/PLAIN-CARDS-SPEC-2026-11-20.md, part R): every layout and kind drawn, tagged and not blank
    /// (a sheet per layout), light ink on every plate, names kept whole and clear of the editor's badges, the text box, foil
    /// regions and the rarity mark, pictures still winning (test pictures it makes itself: flat colour with a stripe, never
    /// card pictures), opponent cards turned back, face-down cards untouched, the time and memory budget, the cache and the
    /// hidden studio. Each step fails on the build before plain cards.
    /// </summary>
    public sealed partial class DuelController
    {
        private IEnumerator PlainCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"plain check: {(ok ? "ok  " : "FAIL")} {what}"); }
            void Note(string what) => Debug.Log($"plain check: note {what}");
            IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }
            IEnumerator Drawn(int most = 600)
            {
                for (int i = 0; i < most && CardFace.DebugQueued > 0; i++) yield return null;
                yield return null;
            }
            string F(double x) => x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            bool Near(Vector4 a, Vector4 b) => (a - b).sqrMagnitude < 1e-6f;
            string V(Vector4 v) => $"({F(v.x)}, {F(v.y)}, {F(v.z)}, {F(v.w)})";

            yield return new WaitForSecondsRealtime(1.5f);
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-mods") < 0)
            {
                Report("needs -mods <empty folder> (it writes its test pictures in its cards/images)", false);
                Application.Quit();
                yield break;
            }
            // The duel the game starts with holds still: its board's own faces mustn't spend the frames this check measures.
            _paused = true;
            UpdateControls();
            var art = CardView.Art!;
            Debug.Log($"plain check: window {Screen.width}x{Screen.height}, canvas scale {F(Mathf.Min(Screen.width / 1920f, Screen.height / 1080f))}");
            if (art.Installed.Count > 0)
            {
                Report($"runs with no pictures (-pics <empty folder> -hdpics off): {art.Installed.Count} found in {string.Join(" + ", art.Folders)}", false);
                Application.Quit();
                yield break;
            }
            ArtLoader.CacheFolder = Path.Combine(folder, "art-cache"); // the check's pictures never reach the game's disk cache
            CardFoil.Override = CardFoil.Display.On;
            CardFoil.FixedLight = new Vector3(0.3f, 0.78f, 0);
            var pack = _cardPack!;

            // ---- The cards: one of each kind in the colour table, and a code with no data ----
            CardInfo? Pick(string name, Func<CardInfo, bool> kind) =>
                pack.All.FirstOrDefault(c => c.Alias == 0 && c.Name == name && kind(c)) ?? pack.All.Where(c => c.Alias == 0 && kind(c)).OrderBy(c => c.Code).FirstOrDefault();
            const uint special = CardType.Ritual | CardType.Fusion | CardType.Synchro | CardType.Xyz | CardType.Link | CardType.Pendulum | CardType.Token;
            const uint spellKinds = 0x10000 | 0x20000 | 0x40000 | 0x80000 | 0x80;
            bool M(CardInfo c) => (c.Type & CardType.Monster) != 0;
            bool S(CardInfo c) => (c.Type & CardType.Spell) != 0;
            bool T(CardInfo c) => (c.Type & CardType.Trap) != 0;
            var kinds = new List<(string label, CardInfo? info)>
            {
                ("Normal", Pick("Dark Magician", c => M(c) && (c.Type & 0x10) != 0 && (c.Type & 0x20) == 0 && (c.Type & special) == 0)),
                ("Effect", Pick("Sangan", c => M(c) && (c.Type & 0x20) != 0 && (c.Type & special) == 0)),
                ("Ritual", Pick("Relinquished", c => M(c) && (c.Type & CardType.Ritual) != 0)),
                ("Fusion", Pick("Elemental HERO Flame Wingman", c => M(c) && (c.Type & CardType.Fusion) != 0)),
                ("Synchro", Pick("Stardust Dragon", c => M(c) && (c.Type & CardType.Synchro) != 0)),
                ("Xyz", Pick("Number 39: Utopia", c => M(c) && (c.Type & CardType.Xyz) != 0)),
                ("Link", Pick("Decode Talker", c => M(c) && (c.Type & CardType.Link) != 0)),
                ("Pendulum", Pick("Odd-Eyes Pendulum Dragon", c => M(c) && (c.Type & CardType.Pendulum) != 0 && (c.Type & CardType.ExtraDeckTypes) == 0)),
                ("Token", Pick("Sheep Token", c => M(c) && (c.Type & CardType.Token) != 0)),
                ("Spell", Pick("Pot of Greed", c => S(c) && (c.Type & spellKinds) == 0)),
                ("Quick-Play", Pick("Mystical Space Typhoon", c => S(c) && (c.Type & 0x10000) != 0)),
                ("Continuous", Pick("Swords of Revealing Light", c => S(c) && (c.Type & 0x20000) != 0)),
                ("Equip", Pick("Axe of Despair", c => S(c) && (c.Type & 0x40000) != 0)),
                ("Field", Pick("Umi", c => S(c) && (c.Type & 0x80000) != 0)),
                ("Ritual Spell", Pick("Advanced Ritual Art", c => S(c) && (c.Type & 0x80) != 0)),
                ("Trap", Pick("Mirror Force", c => T(c) && (c.Type & (0x20000 | 0x100000)) == 0)),
                ("Cont. Trap", Pick("Call of the Haunted", c => T(c) && (c.Type & 0x20000) != 0)),
                ("Counter", Pick("Solemn Judgment", c => T(c) && (c.Type & 0x100000) != 0)),
            };
            uint unknown = 100;
            while (pack.Get(unknown) != null) unknown++;
            Report($"a card of each kind: {string.Join(", ", kinds.Select(k => $"{k.label} {k.info?.Name ?? "(none)"}"))}; unknown code {unknown}",
                kinds.All(k => k.info != null));
            var cards = kinds.Where(k => k.info != null).Select(k => (k.label, code: k.info!.Code, k.info)).ToList();
            cards.Add(("Unknown", unknown, (CardInfo?)null));
            CardInfo Kind(string label) => kinds.First(k => k.label == label).info!;

            var canvasRoot = Ui.Overlay(transform, "PlainCheck", 32000);
            var canvasGo = canvasRoot.parent.gameObject;
            Ui.Image(canvasRoot, Ui.Hex("#0B0E14"));
            RectTransform Page(string name) => Ui.Stretched(name, canvasRoot, Vector2.zero, Vector2.one);
            RawImage Face(RectTransform page, Vector2 center, Vector2 size, float scale, uint code, CardInfo? info, CardFit fit, Rarity rarity, string? label)
            {
                var holder = Ui.Placed($"Face {code}", page, center, size);
                holder.localScale = Vector3.one * scale;
                var raw = holder.gameObject.AddComponent<RawImage>();
                raw.raycastTarget = false;
                CardFace.Draw(raw, code, info, size, fit);
                CardFoil.Apply(raw, info, rarity, 1f);
                if (label != null)
                {
                    var text = Ui.Text(Ui.Placed("Label", page, center + new Vector2(0, -size.y * scale / 2 - 11), new Vector2(Mathf.Max(90, size.x * scale + 8), 20)), label, 13, new Color(1, 1, 1, 0.8f));
                    text.raycastTarget = false;
                }
                return raw;
            }
            Texture2D? lastShot = null;
            IEnumerator Shot(string name)
            {
                yield return new WaitForEndOfFrame();
                if (lastShot != null) Destroy(lastShot);
                lastShot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(folder, $"plain-{name}.png"), lastShot.EncodeToPNG());
            }
            Color32[] Readback(Texture t)
            {
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(t, rt);
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                var px = copy.GetPixels32();
                Destroy(copy);
                return px;
            }
            float Luminance(Color c)
            {
                static float Lin(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
                return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
            }
            float Contrast(Color a, Color b)
            {
                float la = Luminance(a), lb = Luminance(b);
                return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
            }

            // ==== 1. Every layout and kind ==================================================================================
            var sheets = new (string name, CardLayout layout, CardFit fit, Vector2 size, float scale, int perRow)[]
            {
                ("thumb", CardLayout.Thumb, CardFit.Normal, new Vector2(40, 58), 1f, 19),
                ("compact", CardLayout.Compact, CardFit.Normal, new Vector2(88, 128), 1f, 19),
                ("compact-badged", CardLayout.Compact, CardFit.Badged, new Vector2(88, 128), 1f, 19),
                ("standard", CardLayout.Standard, CardFit.Normal, new Vector2(200, 292), 0.85f, 10),
                ("full", CardLayout.Full, CardFit.Normal, new Vector2(360, 525), 0.5f, 10),
            };
            var standardFaces = new Dictionary<string, Texture>();
            foreach (var sheet in sheets)
            {
                var page = Page("Sheet " + sheet.name);
                var faces = new List<(string label, RawImage raw)>();
                float cellW = sheet.size.x * sheet.scale + 12, cellH = sheet.size.y * sheet.scale + 34;
                int rows = (cards.Count + sheet.perRow - 1) / sheet.perRow;
                for (int i = 0; i < cards.Count; i++)
                {
                    int col = i % sheet.perRow, row = i / sheet.perRow;
                    var center = new Vector2((col - (sheet.perRow - 1) / 2f) * cellW, ((rows - 1) / 2f - row) * cellH + 10);
                    var (label, code, info) = cards[i];
                    faces.Add((label, Face(page, center, sheet.size, sheet.scale, code, info, sheet.fit, Rarity.Common, sheet.layout == CardLayout.Thumb ? null : label)));
                }
                yield return Drawn();
                yield return Shot($"sheet-{sheet.name}");
                var problems = new List<string>();
                foreach (var (label, raw) in faces)
                {
                    raw.TryGetComponent<FaceTag>(out var tag);
                    if (tag == null) { problems.Add($"{label} untagged"); continue; }
                    if (!tag.Plain || !CardFace.IsPlain(raw.texture)) problems.Add($"{label} not plain");
                    if (tag.Layout != sheet.layout || CardFace.LayoutOf(raw.texture) != sheet.layout) problems.Add($"{label} is {tag.Layout}");
                    if (sheet.layout == CardLayout.Compact && tag.Fit != sheet.fit) problems.Add($"{label} fit {tag.Fit}");
                    if (CardFace.DebugFlat(lastShot!, raw.rectTransform)) problems.Add($"{label} looks blank");
                    if (sheet.layout == CardLayout.Standard) standardFaces[label] = raw.texture;
                }
                var sample = faces.Count > 0 ? faces[0].raw.texture : null;
                Report($"1. {sheet.name}: {faces.Count} cards ({string.Join(", ", cards.Select(c => c.label))}) tagged, plain, {sheet.layout}{(sheet.fit == CardFit.Badged ? " badged" : "")}, "
                       + $"none blank (texture {sample?.width}x{sample?.height})" + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""),
                    faces.Count == cards.Count && problems.Count == 0);
                Destroy(page.gameObject);
            }

            // ==== 2. Contrast: light ink on every kind's plate =============================================================
            {
                var lines = new List<string>();
                bool ok = standardFaces.Count == cards.Count;
                foreach (var (label, code, info) in cards)
                {
                    var plate = PlainCards.PlateColor(info);
                    float ratio = Contrast(PlainCards.InkLight, plate);
                    // What was drawn: the name plate's bottom right inside corner of the card's Standard face.
                    Color32 drawn = default;
                    if (standardFaces.TryGetValue(label, out var tex))
                    {
                        var px = Readback(tex);
                        int x = Mathf.RoundToInt(0.935f * tex.width), y = Mathf.RoundToInt(0.871f * tex.height);
                        drawn = px[y * tex.width + x];
                    }
                    Color32 want = plate;
                    bool same = Mathf.Abs(drawn.r - want.r) <= 6 && Mathf.Abs(drawn.g - want.g) <= 6 && Mathf.Abs(drawn.b - want.b) <= 6;
                    float drawnRatio = Contrast(PlainCards.InkLight, drawn);
                    lines.Add($"{label} {F(ratio)}:1 (drawn #{ColorUtility.ToHtmlStringRGB(drawn)} {F(drawnRatio)}:1)");
                    if (ratio < 4.5f || drawnRatio < 4.5f || !same) ok = false;
                }
                Report($"2. light ink on each kind's name plate is 4.5:1 or more, and the plate drawn is that colour: {string.Join(", ", lines)}", ok);
            }

            // ==== 3. Names: whole words, clear of the editor's badges ======================================================
            {
                var words = new[] { "Destruction", "Synchron", "Electromagnetic", "Construction", "Thunderstrike", "Airknight", "Afterglow", "Knightmare",
                    "Blackwing", "Archfiend", "Transmigration", "Transmigration", "Polymerization" };
                var names = new List<CardInfo>();
                foreach (var w in words)
                {
                    var found = pack.All.Where(c => c.Alias == 0 && c.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 && !names.Contains(c))
                        .OrderBy(c => c.Name.Length).ThenBy(c => c.Code).FirstOrDefault();
                    if (found != null) names.Add(found);
                }
                var longest = pack.All.FirstOrDefault(c => c.Name == "Number 38: Hope Harbinger Dragon Titanic Galaxy");
                if (longest != null) names.Add(longest);
                // The editor's limit badge (20x20 at 2 px in) and in-deck count (26x22) on its 88x128 result tile, in face
                // fractions (the face sits 3 px in from the tile's edge).
                var badge = Rect.MinMaxRect(-1f / 82, 103f / 122, 19f / 82, 123f / 122);
                var count = Rect.MinMaxRect(58f / 82, 24f / 122, 84f / 82, 46f / 122);
                var textures = new List<(CardInfo info, CardFit fit, Texture tex)>();
                foreach (var info in names)
                    foreach (var fit in new[] { CardFit.Normal, CardFit.Badged })
                        textures.Add((info, fit, CardFace.PlainOnly(info.Code, info, CardLayout.Compact, CardFace.PixelWidth(CardLayout.Compact, 88), fit)));
                yield return Drawn();
                var problems = new List<string>();
                var shown = new List<string>();
                foreach (var (info, fit, tex) in textures)
                {
                    var name = CardFace.DebugName(tex);
                    if (name == null) { problems.Add($"{info.Name} ({fit}): no name laid out"); continue; }
                    problems.AddRange(name.Problems.Select(p => $"{fit}: {p}"));
                    if (fit == CardFit.Badged)
                    {
                        if (name.Rect.Overlaps(badge)) problems.Add($"\"{info.Name}\" under the limit badge ({name.Rect})");
                        if (name.Rect.Overlaps(count)) problems.Add($"\"{info.Name}\" behind the in-deck count ({name.Rect})");
                        shown.Add($"\"{name.Text.Replace("\n", "/")}\" {name.Size}");
                    }
                }
                Report($"3. {names.Count} names at Compact and Compact badged keep their words whole and clear the badge and count: {string.Join(", ", shown)}"
                       + (problems.Count > 0 ? ": " + string.Join("; ", problems) : ""), names.Count >= 13 && problems.Count == 0 && textures.Count == names.Count * 2);
            }

            // ==== 4. The text box ============================================================================================
            {
                var ash = pack.All.FirstOrDefault(c => c.Alias == 0 && c.Name == "Ash Blossom & Joyous Spring");
                var normal = Kind("Normal");
                var pendulum = Kind("Pendulum");
                Report("4. the long-text card is there (Ash Blossom & Joyous Spring)", ash != null);
                if (ash != null)
                {
                    // Drawn while the board shows its text: new characters must not blank the board's (the font atlas trap).
                    canvasGo.SetActive(false);
                    yield return Frames(3);
                    yield return new WaitForEndOfFrame();
                    var lettered = FindObjectsByType<Text>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .Where(t => t.isActiveAndEnabled && t.canvas != null && t.canvas.renderMode == RenderMode.ScreenSpaceOverlay
                                    && t.text.Trim().Length > 0 && t.cachedTextGenerator.characterCountVisible > 0)
                        .Select(t => (t, t.text)).ToList();
                    yield return null;
                    var std = CardFace.PlainOnly(ash.Code, ash, CardLayout.Standard, CardFace.PixelWidth(CardLayout.Standard, 200));
                    var full = CardFace.PlainOnly(ash.Code, ash, CardLayout.Full, CardFace.PixelWidth(CardLayout.Full, 360));
                    var big = CardFace.PlainOnly(ash.Code, ash, CardLayout.Full, 972);
                    var nText = CardFace.PlainOnly(normal.Code, normal, CardLayout.Standard, CardFace.PixelWidth(CardLayout.Standard, 200));
                    var pText = CardFace.PlainOnly(pendulum.Code, pendulum, CardLayout.Standard, CardFace.PixelWidth(CardLayout.Standard, 200));
                    yield return Drawn();
                    yield return new WaitForEndOfFrame();
                    var lost = lettered.Where(e => e.t != null && e.t.isActiveAndEnabled && e.t.text == e.text && e.t.cachedTextGenerator.characterCountVisible == 0)
                        .Select(e => e.text).ToList();
                    yield return Shot("board-atlas");
                    Report($"4. the board's {lettered.Count} lettered texts keep their letters while long-text faces are drawn (big sizes too)"
                           + (lost.Count > 0 ? ": blanked " + string.Join(", ", lost.Take(5)) : ""), lettered.Count > 0 && lost.Count == 0);
                    canvasGo.SetActive(true);
                    var s = CardFace.DebugText(std);
                    var f = CardFace.DebugText(full);
                    var b = CardFace.DebugText(big);
                    var whole = (ash.Text ?? "").Replace("\r\n", "\n").Trim();
                    Report($"4. Ash Blossom at Standard ends in \"…\" inside its box (\"…{(s?.Text.Length > 40 ? s.Text.Substring(s.Text.Length - 40) : s?.Text)}\", all shown {s?.AllShown})",
                        s != null && s.Text.EndsWith("…") && s.AllShown && s.Text.Length > 40);
                    Report($"4. Ash Blossom at Full fits or ends in \"…\" ({f?.Text.Length} of {whole.Length} characters, all shown {f?.AllShown}; at 972 px {b?.Text.Length}, all shown {b?.AllShown})",
                        f != null && f.AllShown && (f.Text.EndsWith("…") || f.Text == whole) && b != null && b.AllShown);
                    var n = CardFace.DebugText(nText);
                    Report($"4. a Normal monster's text ({normal.Name}) is italic", n != null && n.Italic && n.Text.Length > 0);
                    var p = CardFace.DebugText(pText);
                    Report($"4. a Pendulum ({pendulum.Name}) shows its Pendulum effect then its monster text (\"{p?.Pendulum.Substring(0, Math.Min(30, p.Pendulum.Length))}…\" / \"{p?.Text.Substring(0, Math.Min(30, p.Text.Length))}…\")",
                        p != null && p.Pendulum.Length > 10 && p.Text.Length > 10 && !p.Text.Contains("[ ") && !p.Pendulum.Contains("[ ") && !p.Text.Contains("-----"));
                }
            }

            // ==== 5. Foil: plain regions, picture regions, the rarity mark =================================================
            var pictureCodes = new List<uint>();
            void TestPicture(uint code, Color a, Color b)
            {
                const int w = 276, h = 400;
                var t = new Texture2D(w, h, TextureFormat.RGB24, false);
                var px = new Color32[w * h];
                Color32 ca = a, cb = b;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        px[y * w + x] = Mathf.Abs(x - y * 0.69f) < 30 ? cb : ca; // a flat colour with one diagonal stripe
                t.SetPixels32(px);
                t.Apply();
                File.WriteAllBytes(Path.Combine(art.ImagesFolder, $"{code}.png"), t.EncodeToPNG());
                Destroy(t);
                pictureCodes.Add(code);
            }
            var effect = Kind("Effect");
            var pend = Kind("Pendulum");
            var potCard = Kind("Spell");
            TestPicture(effect.Code, new Color(0.2f, 0.5f, 0.8f), new Color(0.9f, 0.9f, 0.95f));
            TestPicture(pend.Code, new Color(0.7f, 0.3f, 0.2f), new Color(0.95f, 0.9f, 0.8f));
            TestPicture(potCard.Code, new Color(0.25f, 0.6f, 0.35f), new Color(0.9f, 0.95f, 0.9f));
            art.Refresh();
            {
                var page = Page("Foil");
                var std = new Vector2(200, 292);
                var normal = Kind("Normal");
                var plainUr = Face(page, new Vector2(-760, 240), std, 0.85f, normal.Code, normal, CardFit.Normal, Rarity.UltraRare, "plain UR");
                var plainRare = Face(page, new Vector2(-570, 240), std, 0.85f, normal.Code, normal, CardFit.Normal, Rarity.Rare, "plain R");
                var plainCommon = Face(page, new Vector2(-380, 240), std, 0.85f, normal.Code, normal, CardFit.Normal, Rarity.Common, "plain N");
                var picUr = Face(page, new Vector2(-190, 240), std, 0.85f, effect.Code, effect, CardFit.Normal, Rarity.UltraRare, "picture UR");
                var picRare = Face(page, new Vector2(0, 240), std, 0.85f, effect.Code, effect, CardFit.Normal, Rarity.Rare, "picture R");
                var picPend = Face(page, new Vector2(190, 240), std, 0.85f, pend.Code, pend, CardFit.Normal, Rarity.UltraRare, "pendulum picture");
                // Every rarity family on plain cards (the light held still): the frame shows them.
                var shown = new[] { Rarity.SuperRare, Rarity.SecretRare, Rarity.UltimateRare, Rarity.GhostRare, Rarity.GoldRare, Rarity.StarlightRare,
                    Rarity.QuarterCenturySecretRare, Rarity.PlatinumRare, Rarity.PrismaticSecretRare, Rarity.CollectorsRare };
                for (int i = 0; i < shown.Length; i++)
                {
                    var (label, code, info) = cards[(i * 2 + 1) % (cards.Count - 1)];
                    Face(page, new Vector2(-855 + i * 190, -230), std, 0.85f, code, info, CardFit.Normal, shown[i], $"{CardFace.RarityShort(shown[i])} {label}");
                }
                for (int i = 0; i < 4; i++) Face(page, new Vector2(400 + i * 100, 300), new Vector2(88, 128), 1f, cards[i].code, cards[i].info, CardFit.Normal, shown[i], CardFace.RarityShort(shown[i]));
                for (int i = 0; i < 4; i++) Face(page, new Vector2(400 + i * 100, 140), new Vector2(40, 58), 1f, cards[i].code, cards[i].info, CardFit.Normal, shown[i + 4], null);
                yield return Drawn();
                CardFoil.FixedLight = new Vector3(0.35f, 0.7f, 0.6f);
                yield return Frames(3);
                yield return Shot("foil");
                CardFoil.FixedLight = new Vector3(0.3f, 0.78f, 0);
                var m = plainUr.material;
                Report($"5. a plain Ultra Rare's material has the plain regions: name {V(m.GetVector("_NameRect"))}, window {V(m.GetVector("_ArtRect"))}, text {V(m.GetVector("_TextRect"))}, "
                       + $"chip {V(m.GetVector("_StarsRect"))}, _Ink {m.GetFloat("_Ink")}, name foil {m.GetFloat("_NameFoil")}",
                    CardFace.IsPlain(plainUr.texture) && Near(m.GetVector("_NameRect"), new Vector4(0.05f, 0.865f, 0.95f, 0.965f)) && Near(m.GetVector("_ArtRect"), new Vector4(0.05f, 0.50f, 0.95f, 0.79f))
                    && Near(m.GetVector("_TextRect"), new Vector4(0.06f, 0.20f, 0.94f, 0.49f)) && m.GetFloat("_Ink") == 1 && m.GetFloat("_NameFoil") > 0);
                var pm = picUr.material;
                bool lightLetters = (effect.Type & (CardType.Spell | CardType.Trap | CardType.Xyz | CardType.Link)) != 0;
                Report($"5. the same Ultra Rare as a test picture (the picture's own texture: {picUr.texture == art.Get(effect.Code)}) keeps today's regions: window {V(pm.GetVector("_ArtRect"))}, text {V(pm.GetVector("_TextRect"))}, "
                       + $"name {V(pm.GetVector("_NameRect"))}, _Ink {pm.GetFloat("_Ink")}",
                    !CardFace.IsPlain(picUr.texture) && picUr.texture == art.Get(effect.Code) && Near(pm.GetVector("_ArtRect"), new Vector4(0.12f, 0.285f, 0.885f, 0.82f))
                    && Near(pm.GetVector("_TextRect"), new Vector4(0.06f, 0.05f, 0.94f, 0.262f)) && Near(pm.GetVector("_NameRect"), new Vector4(0.06f, 0.885f, 0.83f, 0.955f))
                    && pm.GetFloat("_Ink") == (lightLetters ? 1 : 0));
                var dm = picPend.material;
                Report($"5. a Pendulum test picture keeps the Pendulum regions: window {V(dm.GetVector("_ArtRect"))}, text {V(dm.GetVector("_TextRect"))}",
                    !CardFace.IsPlain(picPend.texture) && Near(dm.GetVector("_ArtRect"), new Vector4(0.07f, 0.42f, 0.93f, 0.82f)) && Near(dm.GetVector("_TextRect"), new Vector4(0.04f, 0.05f, 0.96f, 0.42f)));
                string? Mark(RawImage raw) => raw.transform.Find("RarityMark")?.GetComponentInChildren<Text>()?.text;
                bool MarkIsImage(RawImage raw) => raw.transform.Find("RarityMark") is Transform t && t.GetComponent<Image>() != null && t.GetComponent<RawImage>() == null;
                Report($"5. the rarity mark: plain Rare \"{Mark(plainRare)}\", plain Ultra \"{Mark(plainUr)}\", plain Common {(Mark(plainCommon) == null ? "none" : Mark(plainCommon))}, "
                       + $"pictures {(Mark(picUr) == null && Mark(picRare) == null ? "none" : "one")}",
                    Mark(plainRare) == "R" && Mark(plainUr) == "UR" && Mark(plainCommon) == null && Mark(picUr) == null && Mark(picRare) == null && MarkIsImage(plainRare));
                CardFoil.Override = CardFoil.Display.Off;
                CardFoil.Apply(plainRare, normal, Rarity.Rare, 1f);
                Report($"5. the mark shows with foils off (\"{Mark(plainRare)}\")", Mark(plainRare) == "R");
                CardFoil.Override = CardFoil.Display.On;
                Destroy(page.gameObject);
            }

            // ==== 6. Pictures win ===========================================================================================
            {
                var forty = pack.All.Where(c => c.Alias == 0 && M(c) && (c.Type & 0x10) != 0 && (c.Type & 0x20) == 0 && (c.Type & special) == 0 && c.Code < 100000000 && !pictureCodes.Contains(c.Code))
                    .Take(37).Select(c => c.Code).ToList();
                forty.AddRange(pictureCodes);
                canvasGo.SetActive(false); // the editor's own screen in the shot
                _editor.Open(null);
                _editor.DebugSetDeck(forty, Array.Empty<uint>());
                yield return Drawn();
                yield return Frames(3);
                yield return Shot("editor-mixed");
                var (faces, problems) = CardFace.DebugAudit(_editor.transform);
                var pics = faces.Where(f => pictureCodes.Contains(f.Code)).ToList();
                var wrongPics = pics.Where(f => f.Plain || f.GetComponent<RawImage>().texture != art.Get(f.Code)).ToList();
                var wrongPlain = faces.Where(f => !pictureCodes.Contains(f.Code) && !f.Plain).ToList();
                Report($"6. the editor shows the 3 cards with test pictures as those pictures ({pics.Count} faces, {wrongPics.Count} wrong) and the rest plain ({faces.Count - pics.Count} faces, {wrongPlain.Count} not)"
                       + (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(5)) : ""),
                    pics.Select(f => f.Code).Distinct().Count() == 3 && wrongPics.Count == 0 && wrongPlain.Count == 0 && faces.Count - pics.Count >= 30 && problems.Count == 0);
                _editor.Close();
                canvasGo.SetActive(true);
                yield return Frames(2);
            }

            // ==== 7. Turned: the opponent's plain cards read upright; their pictures stay turned ===========================
            {
                var page = Page("Turned");
                FieldCard Field(CardInfo info, uint position) => new FieldCard
                {
                    Code = info.Code, Type = info.Type, Level = info.Level, Rank = info.Level, Attack = info.Attack, Defense = info.Defense,
                    LinkRating = info.Level, Position = position, IsPublic = true,
                };
                var normal = Kind("Normal");
                var mine = CardView.Create(page, new Vector2(-300, 0), CardView.FieldSize, Field(normal, Position.FaceUpAttack), normal, 0, false, null);
                var theirs = CardView.Create(page, new Vector2(-150, 0), CardView.FieldSize, Field(normal, Position.FaceUpAttack), normal, 1, false, null, flip: true);
                var theirPicture = CardView.Create(page, new Vector2(0, 0), CardView.FieldSize, Field(effect, Position.FaceUpAttack), effect, 1, false, null, flip: true);
                var theirSideways = CardView.Create(page, new Vector2(170, 0), CardView.FieldSize, Field(normal, Position.FaceUpDefense), normal, 1, false, null, flip: true);
                var theirHand = CardView.Create(page, new Vector2(330, 0), CardView.FieldSize, Field(normal, Position.FaceUpAttack), normal, 1, false, null, offField: true, flip: true, hand: true);
                foreach (var v in new[] { mine, theirs, theirPicture, theirSideways, theirHand }) v.transform.localScale = Vector3.one * 1.6f;
                yield return Drawn();
                yield return Shot("turned");
                float Z(CardView v) => v.GetComponentInChildren<FaceTag>() is FaceTag tag ? Mathf.Abs(Mathf.DeltaAngle(tag.transform.eulerAngles.z, 0)) : -1;
                bool StripBelow(CardView v)
                {
                    var strip = v.transform.Find("Face/Stats");
                    var face = v.GetComponentInChildren<FaceTag>();
                    return strip != null && face != null && strip.position.y < face.transform.position.y;
                }
                Report($"7. an opponent's plain card is turned back (its face at {F(Z(theirs))}°, the stat strip at its foot {StripBelow(theirs)}); "
                       + $"their picture stays turned ({F(Z(theirPicture))}°); a defence one lies sideways ({F(Z(theirSideways))}°); their revealed hand card {F(Z(theirHand))}°; yours {F(Z(mine))}°",
                    Z(theirs) is >= 0 and < 0.5f && StripBelow(theirs) && Mathf.Abs(Z(theirPicture) - 180) < 0.5f && Mathf.Abs(Z(theirSideways) - 90) < 0.5f
                    && Z(theirHand) is >= 0 and < 0.5f && StripBelow(theirHand) && Z(mine) is >= 0 and < 0.5f && StripBelow(mine));

                // ==== 8. Face-down: the back, untagged; your own set card's peek tag names it ==============================
                var hidden = CardView.Create(page, new Vector2(-150, -250), CardView.FieldSize, Field(normal, Position.FaceDownDefense), normal, 1, false, null, flip: true);
                var ownSet = CardView.Create(page, new Vector2(0, -250), CardView.FieldSize, Field(normal, Position.FaceDownDefense), normal, 0, true, null);
                yield return Frames(2);
                var peek = ownSet.transform.Find("Face/Peek")?.GetComponentInChildren<Text>();
                bool tagged = mine.GetComponentInChildren<FaceTag>() is FaceTag faceUp && faceUp.Plain;
                Report($"8. a set card shows the back, untagged ({hidden.GetComponentInChildren<FaceTag>() == null}, readable {hidden.ShowsFace}); your own set card's peek reads \"{peek?.text}\" "
                       + $"with no face drawn ({ownSet.GetComponentInChildren<FaceTag>() == null}); the same card face-up is a tagged plain card ({tagged})",
                    tagged && hidden.GetComponentInChildren<FaceTag>() == null && !hidden.ShowsFace && ownSet.GetComponentInChildren<FaceTag>() == null && peek?.text == normal.Name
                    && !hidden.DebugFaceDrawn && !ownSet.DebugFaceDrawn);
                Destroy(page.gameObject);
            }
            foreach (var code in pictureCodes) File.Delete(Path.Combine(art.ImagesFolder, $"{code}.png"));
            art.Refresh();
            yield return Frames(2);

            // ==== 9. The budget =============================================================================================
            var plainCodes = pack.All.Where(c => c.Alias == 0 && c.Code < 100000000).OrderBy(c => c.Code).ToList();
            {
                CardFace.Invalidate();
                var compact = plainCodes.Take(120).ToList();
                int start = Time.frameCount;
                foreach (var c in compact) CardFace.PlainOnly(c.Code, c, CardLayout.Compact, 128);
                yield return Drawn();
                var (median, p95, drawn) = CardFace.DebugTimings;
                // (The duel under the check redraws its board meanwhile: its faces count too.)
                Report($"9. 120 Compact faces at 128 px drawn cold: median {F(median)} ms, 95th percentile {F(p95)} ms, {drawn} drawn over {Time.frameCount - start} frames (budget 2 and 5 ms)",
                    drawn >= 120 && median <= 2 && p95 <= 5);
                CardFace.Invalidate();
                yield return Drawn(); // (faces still shown are drawn again after Invalidate: not the pack's)
                var page = Page("Pack");
                var nine = plainCodes.Skip(200).Take(9).ToList();
                // The pack opening turns its cards one at a time (ShopScreen.RevealAll: 0.16 s or more apart): each face is
                // drawn in the frame it appears, none waits in the queue.
                var packFaces = new List<RawImage>();
                var waited = new List<int>();
                for (int i = 0; i < 9; i++)
                {
                    packFaces.Add(Face(page, new Vector2(-760 + i * 190, 0), new Vector2(200, 292), 0.85f, nine[i].Code, nine[i], CardFit.Normal, Rarity.Common, null));
                    if (CardFace.DebugQueued > 0) waited.Add(i);
                    yield return Frames(2);
                }
                var after = CardFace.DebugTimings;
                Report($"9. a pack's 9 Standard faces ({packFaces[0].texture.width} px), turned one by one, are each drawn in the frame they appear ({9 - waited.Count} of 9; "
                       + $"median {F(after.medianMs)} ms, 95th {F(after.p95Ms)} ms)", waited.Count == 0 && after.drawn >= 9);
                yield return Shot("pack");
                CardFace.Invalidate();
                yield return Drawn(); // (faces still shown are drawn again after Invalidate: not the burst's)
                var burst = new List<RawImage>();
                for (int i = 0; i < 9; i++) burst.Add(Face(page, new Vector2(-760 + i * 190, -300), new Vector2(200, 292), 0.5f, nine[i].Code, nine[i], CardFit.Normal, Rarity.Common, null));
                int burstQueued = CardFace.DebugQueued;
                int burstFrame = Time.frameCount;
                yield return Drawn();
                Note($"9 Standard faces asked for in one frame: {9 - burstQueued} drawn at once, the rest {Time.frameCount - burstFrame - 1} frame(s) later (the 6 ms frame budget)");
                Destroy(page.gameObject);
                CardFace.Invalidate();
                var one = plainCodes[300];
                CardFace.PlainOnly(one.Code, one, CardLayout.Full, CardFace.PixelWidth(CardLayout.Full, 360));
                yield return Drawn();
                Note($"1 Full face cold at {CardFace.PixelWidth(CardLayout.Full, 360)} px: {F(CardFace.DebugTimings.medianMs)} ms");

                // The editor on a 60-card deck, from an empty cache: fully drawn within 20 frames.
                CardFace.Invalidate();
                var sixty = plainCodes.Where(c => M(c) && !c.IsExtraDeckMonster || S(c) || T(c)).Skip(400).Take(60).Select(c => c.Code).ToList();
                canvasGo.SetActive(false);
                int opened = Time.frameCount;
                _editor.Open(null);
                _editor.DebugSetDeck(sixty, Array.Empty<uint>());
                int queued = CardFace.DebugQueued;
                while (CardFace.DebugQueued > 0 && Time.frameCount - opened < 300) yield return null;
                int frames = Time.frameCount - opened;
                var timings = CardFace.DebugTimings;
                yield return Frames(2);
                yield return Shot("editor");
                var (faces, problems) = CardFace.DebugAudit(_editor.transform);
                Report($"9. the editor opened on a 60-card deck is fully drawn within 20 frames ({frames} frames; {queued} queued at first; {timings.drawn} drawn, median {F(timings.medianMs)} ms; "
                       + $"{faces.Count} faces, {faces.Count(f => f.Plain)} plain)" + (problems.Count > 0 ? ": " + string.Join("; ", problems.Take(5)) : ""),
                    frames <= 20 && faces.Count >= 60 && faces.All(f => f.Plain) && problems.Count == 0 && CardFace.DebugQueued == 0);
                long editorBytes = CardFace.DebugBytes;
                _editor.Close();
                canvasGo.SetActive(true);
                // The title wall's fill (Part S: Standard plain cards) and a pack, on top of the editor's.
                var wall = Page("Wall");
                var wallCodes = plainCodes.Skip(600).Take(48).ToList();
                for (int i = 0; i < wallCodes.Count; i++)
                    Face(wall, new Vector2(-880 + (i % 12) * 160, 400 - (i / 12) * 250), new Vector2(200, 292), 0.7f, wallCodes[i].Code, wallCodes[i], CardFit.Normal, (Rarity)(i % 3 == 0 ? (int)Rarity.UltraRare : 0), null);
                for (int i = 0; i < 9; i++) CardFace.PlainOnly(nine[i].Code, nine[i], CardLayout.Standard, CardFace.PixelWidth(CardLayout.Standard, 200));
                yield return Drawn();
                yield return Shot("wall");
                Report($"9. the plain cache after the editor ({F(editorBytes / 1048576.0)} MB), a title wall of 48 and a pack: {F(CardFace.DebugBytes / 1048576.0)} MB (cap 96 MB)",
                    CardFace.DebugBytes <= CardFace.CacheCap && CardFace.DebugBytes > editorBytes);
                Destroy(wall.gameObject);
                yield return Frames(2);
            }

            // ==== 10. The cache =============================================================================================
            {
                CardFace.Invalidate();
                var card = Kind("Effect");
                var first = CardFace.PlainOnly(card.Code, card, CardLayout.Compact, 128);
                var second = CardFace.PlainOnly(card.Code, card, CardLayout.Compact, 128);
                yield return Drawn();
                var before = Readback(first);
                CardFace.Invalidate();
                yield return null;
                bool dropped = first == null; // Unity's null: the texture was destroyed
                var again = CardFace.PlainOnly(card.Code, card, CardLayout.Compact, 128);
                yield return Drawn();
                var redrawn = Readback(again);
                int differ = 0;
                for (int i = 0; i < Math.Min(before.Length, redrawn.Length); i++)
                    if (Mathf.Abs(before[i].r - redrawn[i].r) > 2 || Mathf.Abs(before[i].g - redrawn[i].g) > 2 || Mathf.Abs(before[i].b - redrawn[i].b) > 2 || before[i].a != redrawn[i].a) differ++;
                bool opaque = redrawn.All(p => p.a == 255);
                Report($"10. the same card at the same size twice is one texture ({ReferenceEquals(first, second)}); Invalidate drops it ({dropped}); the redraw is a new texture "
                       + $"({!ReferenceEquals(first, again)}) that matches ({differ} of {redrawn.Length} pixels differ; opaque {opaque})",
                    ReferenceEquals(first, second) && dropped && !ReferenceEquals(first, again) && before.Length == redrawn.Length && differ == 0 && opaque);
                // Past the cap the least recently used go, and their textures are released.
                CardFace.Invalidate();
                var big = plainCodes.Skip(700).Take(20).Select(c => CardFace.PlainOnly(c.Code, c, CardLayout.Full, 972)).ToList();
                yield return Drawn();
                int released = big.Count(t => t == null);
                Report($"10. 20 Full faces at 972 px ({F(PlainCards.BytesOf(big[big.Count - 1]) / 1048576.0)} MB each): the cache stays at {F(CardFace.DebugBytes / 1048576.0)} MB (cap 96), "
                       + $"{released} least recently used released, the newest kept ({big[big.Count - 1] != null})",
                    CardFace.DebugBytes <= CardFace.CacheCap && released > 0 && big[0] == null && big[big.Count - 1] != null);
                CardFace.Invalidate();
            }

            // ==== 11. The hidden studio =====================================================================================
            {
                var (exists, cameraOn, canvasOn, raycaster, seeing) = PlainCards.DebugStudio();
                Report($"11. the studio exists ({exists}); its camera never renders by itself (enabled {cameraOn}), its canvas is off between draws ({canvasOn}), "
                       + $"it takes no input (raycaster {raycaster}) and no other camera sees its layer ({seeing})",
                    exists && !cameraOn && !canvasOn && !raycaster && seeing == 0);
            }

            // ==== 12. The cap on big screens: the title wall, the main menu's face and 90 editor tiles ====================
            {
                var lines = new List<string>();
                bool ok = true;
                foreach (var (label, sw, sh) in new[] { ("3024x1964", 3024f, 1964f), ("3840x2160", 3840f, 2160f) })
                {
                    CardFace.Invalidate();
                    CardFace.DebugScale = Mathf.Min(sw / 1920f, sh / 1080f); // the sizes such a screen asks for, through the same ladder
                    var page = Page("Cap " + label);
                    var wallPage = Ui.Stretched("Wall", page, Vector2.zero, Vector2.one);
                    var faces = new List<RawImage>();
                    var wall = new List<RawImage>();
                    RawImage Img(RectTransform parent, Vector2 size)
                    {
                        var raw = Ui.Placed("Face", parent, Vector2.zero, size).gameObject.AddComponent<RawImage>();
                        raw.raycastTarget = false;
                        faces.Add(raw);
                        return raw;
                    }
                    var wallSize = new Vector2(236, 344);
                    foreach (var c in plainCodes.Skip(100).Take(54))
                    {
                        var raw = Img(wallPage, wallSize);
                        raw.texture = CardFace.WallFace(c.Code, c, wallSize); // as TitleScreen.DrawWall does
                        CardFace.Tag(raw, c.Code, wallSize, CardFit.Normal, c);
                        wall.Add(raw);
                    }
                    var menu = plainCodes[200];
                    CardFace.Draw(Img(page, new Vector2(380, 554)), menu.Code, menu, new Vector2(380, 554));
                    foreach (var (c, i) in plainCodes.Skip(300).Take(90).Select((c, i) => (c, i)))
                    {
                        var size = i < 25 ? new Vector2(88, 128) : new Vector2(62, 90);
                        CardFace.Draw(Img(page, size), c.Code, c, size, CardFit.Badged);
                    }
                    yield return Drawn();
                    long shownBytes = CardFace.DebugBytes;
                    bool kept = faces.All(f => f.texture != null);
                    // Hidden, the wall pins nothing: other faces may take its room; shown again, it's drawn again.
                    wallPage.gameObject.SetActive(false);
                    var others = plainCodes.Skip(500).Take(40).Select(c => CardFace.PlainOnly(c.Code, c, CardLayout.Full, 972)).ToList();
                    yield return Drawn();
                    int evicted = wall.Count(w => w.texture == null);
                    long busyBytes = CardFace.DebugBytes;
                    wallPage.gameObject.SetActive(true);
                    yield return Drawn();
                    bool back = wall.All(w => w.texture != null && CardFace.IsPlain(w.texture));
                    lines.Add($"{label}: {F(shownBytes / 1048576.0)} MB shown (wall faces {wall[0].texture?.width} px), every face kept {kept}; "
                              + $"wall hidden, 40 more faces: {evicted} of {wall.Count} wall faces let go, {F(busyBytes / 1048576.0)} MB; shown again, all drawn {back}");
                    ok &= shownBytes <= CardFace.CacheCap && kept && evicted > 0 && busyBytes <= CardFace.CacheCap && back;
                    Destroy(page.gameObject);
                    yield return null;
                }
                CardFace.DebugScale = null;
                CardFace.Invalidate();
                Report($"12. the cache holds its 96 MB cap on big screens: {string.Join("; ", lines)}", ok);
            }

            // ==== 13. The blank test: a flat face turned 12°, and one half off screen, read blank; a plain card doesn't ======
            {
                var page = Page("Flat");
                Ui.Image(page, Ui.Hex("#0B0E14"));
                int k = 0;
                for (int x = -900; x <= 900; x += 100)
                    for (int y = -480; y <= 480; y += 140)
                    {
                        var c = plainCodes[k++ % plainCodes.Count];
                        var bg = Ui.Placed("Bg", page, new Vector2(x, y), new Vector2(88, 128)).gameObject.AddComponent<RawImage>();
                        CardFace.Draw(bg, c.Code, c, new Vector2(88, 128));
                    }
                RawImage Flat(Vector2 at, float turn)
                {
                    var raw = Ui.Placed("FlatFace", page, at, new Vector2(240, 350)).gameObject.AddComponent<RawImage>();
                    raw.texture = Texture2D.whiteTexture;
                    raw.color = Ui.Hex("#7A5A3A");
                    raw.rectTransform.localRotation = Quaternion.Euler(0, 0, turn);
                    return raw;
                }
                var turned = Flat(new Vector2(-300, 0), 12);
                var offScreen = Flat(new Vector2(960, 0), 0);
                var real = Ui.Placed("Real", page, new Vector2(300, 0), new Vector2(240, 350)).gameObject.AddComponent<RawImage>();
                CardFace.Draw(real, Kind("Effect").Code, Kind("Effect"), new Vector2(240, 350));
                real.rectTransform.localRotation = Quaternion.Euler(0, 0, 12);
                yield return Drawn();
                yield return Shot("flat-test");
                bool a = CardFace.DebugFlat(lastShot!, turned.rectTransform), b = CardFace.DebugFlat(lastShot!, offScreen.rectTransform), c2 = CardFace.DebugFlat(lastShot!, real.rectTransform);
                Report($"13. the blank test reads a flat face turned 12° blank ({a}), a flat face half off screen blank ({b}), a plain card turned 12° not blank ({!c2})", a && b && !c2);
                Destroy(page.gameObject);
                yield return null;
            }

            // ==== 14. Invalidate with faces queued and shown: all drawn again, the same bytes =============================
            {
                CardFace.Invalidate();
                yield return Drawn();
                var page = Page("Invalidate");
                var faces = new List<RawImage>();
                foreach (var c in plainCodes.Skip(900).Take(40))
                {
                    var raw = Ui.Placed("Face", page, Vector2.zero, new Vector2(88, 128)).gameObject.AddComponent<RawImage>();
                    CardFace.Draw(raw, c.Code, c, new Vector2(88, 128));
                    faces.Add(raw);
                }
                int queued = CardFace.DebugQueued;
                long bytes = CardFace.DebugBytes;
                CardFace.Invalidate();
                long right = CardFace.DebugBytes;
                yield return Drawn();
                int drawn = faces.Count(f => f.texture != null && CardFace.DebugName(f.texture) != null);
                Report($"14. Invalidate with {queued} of {faces.Count} faces queued and all shown: {drawn} drawn again, {F(CardFace.DebugBytes / 1048576.0)} MB after "
                       + $"({F(bytes / 1048576.0)} before, {F(right / 1048576.0)} right after)",
                    queued > 0 && drawn == faces.Count && CardFace.DebugBytes == bytes && right == bytes && CardFace.DebugQueued == 0);
                Destroy(page.gameObject);
                yield return null;
                CardFace.Invalidate();
            }

            // ==== 15. Rarity marks: one per rarity, short ===================================================================
            {
                var all = ((Rarity[])Enum.GetValues(typeof(Rarity))).Select(r => (r, mark: CardFace.RarityShort(r))).ToList();
                var twice = all.GroupBy(x => x.mark).Where(g => g.Count() > 1).Select(g => $"{g.Key}: {string.Join("/", g.Select(x => x.r))}").ToList();
                var longMarks = all.Where(x => x.mark.Length > 5 || x.mark.Length == 0).Select(x => x.mark).ToList();
                Report($"15. every rarity has its own mark of at most 5 letters ({string.Join(" ", all.Select(x => x.mark))})"
                       + (twice.Count > 0 ? "; shared: " + string.Join("; ", twice) : "") + (longMarks.Count > 0 ? "; too long: " + string.Join(", ", longMarks) : ""),
                    twice.Count == 0 && longMarks.Count == 0);
            }

            // ==== 16. The font atlas: never rebuilt while a face is laid out ================================================
            {
                var longText = plainCodes.Where(c => (c.Text?.Length ?? 0) > 500).ToList();
                CardFace.Invalidate();
                int before = PlainCards.DebugMidDrawRebuilds;
                foreach (var c in longText.Take(10))
                {
                    CardFace.PlainOnly(c.Code, c, CardLayout.Full, 972);
                    CardFace.PlainOnly(c.Code, c, CardLayout.Standard, 432);
                }
                yield return Drawn();
                int with = PlainCards.DebugMidDrawRebuilds - before;
                // The count sees a rebuild when one happens while a face is drawn (forced: many new characters at big sizes).
                before = PlainCards.DebugMidDrawRebuilds;
                PlainCards.DebugRebuildMidDraw();
                int without = PlainCards.DebugMidDrawRebuilds - before;
                CardFace.Invalidate();
                Report($"16. the font atlas is rebuilt 0 times while 20 long-text faces are laid out ({with}); a forced mid-draw rebuild is counted ({without})",
                    with == 0 && without > 0);
            }

            CardFoil.FixedLight = null;
            CardFoil.Override = null;
            if (lastShot != null) Destroy(lastShot);
            Destroy(canvasGo);
            Debug.Log($"plain check done: {bad} failure(s)");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
