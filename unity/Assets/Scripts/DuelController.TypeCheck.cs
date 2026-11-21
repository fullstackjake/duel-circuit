using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-typecheck &lt;folder&gt;` (absolute; with -mods &lt;empty folder&gt;, -pics &lt;empty folder&gt; -hdpics off): the fonts
    /// (docs/dev/TYPE-AND-FONTS-SPEC-2026-11-21.md, section 7).
    /// <list type="number">
    /// <item>Ui.Font is Barlow Regular; each face loads its own file; the alley's stencil lookup (Resources/Fonts) finds none.</item>
    /// <item>Every character of glyph-corpus.txt and of every card's name, text and strings is drawn by each face's chain
    /// (a character no font has is reported as missing: the check can fail); which font draws each symbol; a sheet per face.</item>
    /// <item>The corpus carries its hash (its freshness is --texts-check's, in the harness).</item>
    /// <item>Strong picks SemiBold (Condensed Bold for condensed and display text), Italic picks Italic, Weight puts back the face
    /// the text had; no text on the title, the menu, Settings, About, the campaign, the shop, the van, the board or the concede
    /// dialog sets FontStyle Bold or Italic, and none draws in the built-in font; hero numbers are marked.</item>
    /// <item>TabularDigits: "1111" and "8888" put their digits at the same places; an LP roll's digits don't move sideways;
    /// without it they do.</item>
    /// <item>Every script line with rich-text &lt;b&gt; or &lt;i&gt; (a faux weight) is listed from the corpus, and those on the
    /// screens seen: a note, not a failure, until R2 ends.</item>
    /// <item>About credits the fonts; LicenseFiles finds both font licences; the third-party reader shows them after the notices.</item>
    /// </list>
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>The symbols the spec names (section 3, "Glyph coverage").</summary>
        private const string TypeSymbols = "★☆▲▼◆✦→×·…⊘⬢";

        /// <summary>Of the symbols the check asks about: the ones Barlow has, and the ones Barlow lacks that Noto Sans Symbols 2
        /// has (from the font files' character maps; → ← ⊘ ─ are in neither, so the system's fonts draw them; − ≈ × · … are Barlow's).</summary>
        private const string BarlowHas = "×·…−≈", NotoHas = "★☆▲▼◆✦⬢♛✓✗✕⛓●○■▶▸";

        private IEnumerator TypeCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            int bad = 0;
            void Report(string what, bool ok) { if (!ok) bad++; Debug.Log($"type check: {(ok ? "ok  " : "FAIL")} {what}"); }
            void Note(string what) => Debug.Log($"type check: note {what}");
            IEnumerator Shot(string name)
            {
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"type-{name}.png"));
                yield return null;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1.5f);
            Note($"screen {Screen.width}x{Screen.height}, platform {Application.platform}, editor {Application.isEditor}");

            // ---- 1. The fonts load ----
            var faces = System.Enum.GetValues(typeof(Face)).Cast<Face>().ToList();
            var files = new Dictionary<Face, string>
            {
                [Face.Body] = "Barlow-Regular", [Face.Italic] = "Barlow-Italic", [Face.Strong] = "Barlow-SemiBold",
                [Face.Condensed] = "BarlowCondensed-SemiBold", [Face.CondensedBold] = "BarlowCondensed-Bold",
            };
            Report($"Ui.Font is Barlow Regular ({Ui.Font.name}, dynamic {Ui.Font.dynamic})", Ui.Font.name == "Barlow-Regular" && !ReferenceEquals(Ui.Font, Typography.Builtin));
            foreach (var face in faces)
            {
                var f = Typography.Font(face);
                Report($"{face} loads {files[face]} ({f.name}; fallbacks: {string.Join(", ", f.fontNames ?? new string[0])})",
                    f.name == files[face] && !ReferenceEquals(f, Typography.Builtin) && f.dynamic);
            }
            Report($"the symbol fallback loads ({Typography.Symbols?.name ?? "missing"})", Typography.Symbols != null && Typography.Symbols.name == "NotoSansSymbols2-Regular");
            Report($"the alley's stencil lookup finds no font in Resources/Fonts ({Resources.LoadAll<Font>("Fonts").Length}; TitleFont {UiTheme.BackAlley.TitleFont?.name ?? "none"})",
                Resources.LoadAll<Font>("Fonts").Length == 0 && UiTheme.BackAlley.TitleFont == null);

            // ---- 2. Coverage ----
            var corpusAsset = Resources.Load<TextAsset>("glyph-corpus");
            var corpus = new List<(char c, string from)>();
            string hashLine = "";
            if (corpusAsset != null)
                foreach (var line in corpusAsset.text.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.StartsWith("# chars-sha256: ")) hashLine = line.Substring(16).Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var tab = line.IndexOf('\t');
                    var ch = tab > 0 ? line.Substring(0, tab) : line;
                    foreach (var c in ch) corpus.Add((c, tab > 0 ? line.Substring(tab + 1) : "?"));
                }
            Report($"glyph-corpus.txt loads: {corpus.Count} characters", corpusAsset != null && corpus.Count >= 30);
            // ---- 3. Its hash (the harness checks it against the sources) ----
            Report($"the corpus carries its characters' hash ({(hashLine.Length >= 12 ? hashLine.Substring(0, 12) : "none")}; --texts-check fails when it is stale)", hashLine.Length == 64);

            var from = new Dictionary<char, string>();
            foreach (var (c, f) in corpus) if (!from.ContainsKey(c)) from[c] = f;
            int cards = 0;
            if (_cardPack != null)
                foreach (var card in _cardPack.All)
                {
                    cards++;
                    foreach (var s in new[] { card.Name, card.Text }.Concat(card.Strings ?? new string[0]))
                        if (s != null)
                            foreach (var c in s)
                                if (c > 0x7F && !from.ContainsKey(c)) from[c] = $"card {card.Code} ({card.Name})";
                }
            var all = new string(from.Keys.OrderBy(c => c).ToArray());
            Note($"{all.Length} non-ASCII characters to draw ({corpus.Count} from the corpus, the rest from {cards} cards)");
            foreach (var face in faces)
            {
                var missing = Typography.Missing(all, face);
                var shown = missing.Select(c => $"U+{(int)c:X4} {(char.IsSurrogate(c) ? "(half of a pair)" : c.ToString())} from {from[c]}").Distinct().Take(12);
                Report($"{face}: every character is drawn by its chain ({all.Length - missing.Count} of {all.Length}){(missing.Count > 0 ? ": missing " + string.Join("; ", shown) : "")}", missing.Count == 0);
            }
            // The control: a private-use character no font has must come out missing, or the coverage test proves nothing.
            var control = Typography.Missing(((char)0xE8FF).ToString(), Face.Body); // (not a literal: the corpus would list it)
            Report($"the coverage test can fail: U+E8FF (private use) is missing from Body's chain ({control.Count == 1})", control.Count == 1);

            // Which font draws each symbol: the face's advance against Noto Sans Symbols 2's and the built-in font's.
            const int px = 64;
            var noto = Typography.Symbols;
            var drawnBy = new List<string>();
            foreach (var c in TypeSymbols + "♛✓✗✕⛓●○■▶▸←−≈")
            {
                var body = Typography.DebugAdvance(Typography.Font(Face.Body), c, px);
                var cond = Typography.DebugAdvance(Typography.Font(Face.CondensedBold), c, px);
                var inNoto = noto != null && noto.HasCharacter(c) ? Typography.DebugAdvance(noto, c, px) : 0;
                var builtin = Typography.DebugAdvance(Typography.Builtin, c, px);
                // Barlow's own characters (its cmap, read offline) have their own advance in each face; a fallback's glyph has
                // the fallback's advance whichever Barlow face asks.
                bool own = BarlowHas.IndexOf(c) >= 0;
                string who = own ? "Barlow" : NotoHas.IndexOf(c) >= 0 && inNoto > 0 && body == inNoto && cond == inNoto ? "Noto Sans Symbols 2" : body > 0 ? "a system font" : "nothing";
                drawnBy.Add($"{c} {who} (Barlow {body}, Condensed Bold {cond}, Noto {inNoto}, built-in {builtin})");
            }
            Note("who draws each symbol at 64 px: " + string.Join("; ", drawnBy));
            int byNoto = drawnBy.Count(d => d.Contains("Noto Sans Symbols 2 ("));
            Report($"the project font Noto Sans Symbols 2 draws every symbol here that Barlow lacks and it has ({byNoto} of {NotoHas.Length}: plan A, the import settings' fallback, works in this player)", byNoto == NotoHas.Length);

            // Every character Barlow and Noto both lack: which of the faces' named system fonts could draw it (their advance at
            // 64 px matches the chain's). On the PC this answers the Windows question.
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            var chainNames = (Typography.Font(Face.Body).fontNames ?? new string[0]).Where(n => n != "Barlow" && n != "Noto Sans Symbols 2").ToList();
            var osFonts = chainNames.Where(installed.Contains).Select(n => (name: n, font: Font.CreateDynamicFontFromOSFont(n, px))).ToList();
            Note($"the faces' system fallbacks: {string.Join(", ", chainNames.Select(n => installed.Contains(n) ? n : n + " (not installed)"))}");
            var systemDrawn = new List<string>();
            foreach (var c in all)
            {
                if (char.IsWhiteSpace(c) || char.IsSurrogate(c) || char.IsControl(c) || NotoHas.IndexOf(c) >= 0) continue;
                // A fallback's glyph has the same advance whichever Barlow face asks; Barlow's own glyphs differ between
                // Regular and Condensed Bold. So equal advances mean a fallback drew it.
                var body = Typography.DebugAdvance(Typography.Font(Face.Body), c, px);
                var cond = Typography.DebugAdvance(Typography.Font(Face.CondensedBold), c, px);
                if (body <= 0 || body != cond) continue;
                // A system font made by name falls back on the system's own fonts too, so several names can match: all are listed.
                var matches = osFonts.Where(o => Typography.DebugAdvance(o.font, c, px) == body).Select(o => o.name).ToList();
                systemDrawn.Add($"U+{(int)c:X4} {c}: width matches {(matches.Count > 0 ? string.Join(" / ", matches) : "none of the named fonts (the system's default draws it)")} ({from[c]})");
            }
            Note($"characters drawn by a system font ({systemDrawn.Count}): {string.Join("; ", systemDrawn)}");

            // A sheet per face: the spec's symbols big, every character to draw, a sample line.
            var sheetRoot = Ui.Overlay(transform, "TypeSheet", 900);
            var sheetCanvas = sheetRoot.GetComponentInParent<Canvas>();
            foreach (var face in faces.Concat(new Face[] { (Face)(-1) }))
            {
                for (int i = sheetRoot.childCount - 1; i >= 0; i--) DestroyImmediate(sheetRoot.GetChild(i).gameObject);
                Ui.Image(Ui.Stretched("Back", sheetRoot, Vector2.zero, Vector2.one), new Color(0.07f, 0.08f, 0.11f));
                bool builtin = (int)face < 0;
                var font = builtin ? Typography.Builtin : Typography.Font(face);
                Text Line(float y, float h, string s, int size, Color color)
                {
                    var t = Ui.Text(Ui.Box(sheetRoot, 60, y, 1800, h), s, size, color, TextAnchor.UpperLeft);
                    t.font = font;
                    t.supportRichText = false;
                    return t;
                }
                var label = Ui.Text(Ui.Box(sheetRoot, 60, 20, 1800, 40), builtin ? "Built-in font (LegacyRuntime), for comparison" : $"{face}: {font.name}", 26, new Color(1, 0.79f, 0.25f), TextAnchor.UpperLeft);
                label.supportRichText = false;
                Line(80, 120, TypeSymbols, 84, Color.white);
                Line(210, 240, string.Join(" ", corpus.Select(c => c.c).Distinct()), 44, Color.white);
                Line(460, 60, "Victory!  VS  CAMPAIGN  Ryo, the Rookie  LV 4  8,000 LP  12,345 DP  ★★☆", 44, Color.white);
                Line(530, 60, "0123456789  The quick brown fox jumps over the lazy dog.  Æther Œuvre façade naïve", 36, new Color(0.85f, 0.88f, 0.95f));
                Line(600, 400, "Once per turn: you can target 1 face-up monster on the field; change its battle position. If this card is "
                    + "destroyed by battle → draw 1 card. (Quick Effect) ⊘ ⬢ ◆ ✦ × · …", 28, new Color(0.85f, 0.88f, 0.95f));
                yield return Shot($"symbols-{(builtin ? "builtin" : face.ToString().ToLowerInvariant())}");
            }
            Destroy(sheetCanvas.gameObject);

            // ---- 4. Weights are faces ----
            var probeRoot = Ui.Overlay(transform, "TypeProbe", 901);
            var probeCanvas = probeRoot.GetComponentInParent<Canvas>();
            Text Probe(int size, Face? face = null)
            {
                var t = Ui.Text(Ui.Box(probeRoot, 100, 100, 900, 120), "Probe", size, Color.white, TextAnchor.MiddleLeft);
                if (face is Face f) t.font = Typography.Font(f);
                return t;
            }
            var strongBody = Typography.Strong(Probe(18));
            var strongCond = Typography.Strong(Probe(18, Face.Condensed));
            var strongTitle = Typography.Strong(Probe(34));
            var italic = Typography.Italic(Probe(18));
            var back = Typography.Weight(Typography.Strong(Probe(18)), false);
            var backCond = Typography.Weight(Probe(18, Face.CondensedBold), false);
            var title = UiTheme.Current.Title(Probe(26));
            Report($"Strong on Barlow is SemiBold ({strongBody.font.name}), on Condensed is Condensed Bold ({strongCond.font.name}), at 34 is Condensed Bold ({strongTitle.font.name}); all upright",
                strongBody.font == Typography.Font(Face.Strong) && strongCond.font == Typography.Font(Face.CondensedBold) && strongTitle.font == Typography.Font(Face.CondensedBold)
                && new[] { strongBody, strongCond, strongTitle }.All(t => t.fontStyle == FontStyle.Normal));
            Report($"Italic is Barlow Italic ({italic.font.name}, {italic.fontStyle}); Weight(false) goes back to Barlow ({back.font.name}) and leaves a text never made strong as it was ({backCond.font.name}); a Main Street title is Condensed Bold ({title.font.name})",
                italic.font == Typography.Font(Face.Italic) && italic.fontStyle == FontStyle.Normal && back.font == Typography.Font(Face.Body)
                && backCond.font == Typography.Font(Face.CondensedBold) && title.font == Typography.Font(Face.CondensedBold));
            // The face it had comes back, at 18 and at 34; Strong again after a size change picks again.
            var bodyBig = Typography.Strong(Probe(34));
            var bodyBigFont = bodyBig.font;
            Typography.Weight(bodyBig, false);
            var condBack = Typography.Weight(Typography.Strong(Probe(18, Face.Condensed)), false);
            var italicBack = Typography.Weight(Typography.Strong(Typography.Italic(Probe(18))), false);
            var resized = Typography.Strong(Probe(18));
            var at18 = resized.font;
            resized.fontSize = 34;
            Typography.Strong(resized);
            var at34 = resized.font;
            resized.fontSize = 18;
            Typography.Strong(resized);
            var at18Again = resized.font;
            Report($"Weight(false) restores the face it had: Barlow at 34 ({bodyBigFont.name} → {bodyBig.font.name}), Condensed at 18 ({condBack.font.name}), Italic ({italicBack.font.name}); "
                   + $"Strong after a size change picks again ({at18.name} at 18, {at34.name} at 34, {at18Again.name} at 18)",
                bodyBigFont == Typography.Font(Face.CondensedBold) && bodyBig.font == Typography.Font(Face.Body) && condBack.font == Typography.Font(Face.Condensed)
                && italicBack.font == Typography.Font(Face.Italic) && at18 == Typography.Font(Face.Strong) && at34 == Typography.Font(Face.CondensedBold) && at18Again == Typography.Font(Face.Strong));
            Report($"the built-in font is no face (FaceOfFont: {Typography.FaceOfFont(Typography.Builtin)?.ToString() ?? "none"}); every face's file is in the build",
                Typography.FaceOfFont(Typography.Builtin) == null && faces.All(Typography.Has));
            var roleText = Ui.Text(Ui.Box(probeRoot, 100, 300, 900, 60), "Label", TextRole.Label, Color.white);
            Report($"Ui.Text at a role: Label is {roleText.fontSize} in {roleText.font.name}; RoleFor 13 → {UiTheme.RoleFor(13)}, 15 → {UiTheme.RoleFor(15)}, 24 → {UiTheme.RoleFor(24)}, 47 → {UiTheme.RoleFor(47)}; Size(Body, reading) {UiTheme.Size(TextRole.Body, true)} at text scale {FxSettings.TextScale}",
                roleText.fontSize == 20 && roleText.font == Typography.Font(Face.Condensed) && UiTheme.RoleFor(13) == TextRole.Caption && UiTheme.RoleFor(15) == TextRole.Small
                && UiTheme.RoleFor(24) == TextRole.Heading && UiTheme.RoleFor(47) == TextRole.Display && UiTheme.Floor == 14
                && UiTheme.Size(TextRole.Body, true) == Mathf.RoundToInt(18 * FxSettings.TextScale) && UiTheme.Size(TextRole.Label, true) == 20);

            // ---- 5. Fixed-width digits ----
            var counter = Typography.Counter(Typography.Strong(Ui.Text(Ui.Box(probeRoot, 100, 420, 900, 80), "1111", 50, Color.white, TextAnchor.MiddleLeft)));
            counter.horizontalOverflow = HorizontalWrapMode.Overflow;
            var plain = Typography.Strong(Ui.Text(Ui.Box(probeRoot, 100, 520, 900, 80), "1111", 50, Color.white, TextAnchor.MiddleLeft));
            plain.horizontalOverflow = HorizontalWrapMode.Overflow;
            plain.gameObject.AddComponent<TabularDigits>(); // measures only: switched off below
            List<float> Centers(Text t, string s, bool on)
            {
                var effect = t.GetComponent<TabularDigits>();
                effect.enabled = true;
                t.text = s;
                Canvas.ForceUpdateCanvases();
                effect.PrepareNow(); // what LateUpdate does before a frame's rebuild
                t.SetVerticesDirty();
                Canvas.ForceUpdateCanvases();
                var got = effect.DebugCenters.ToList();
                bool applied = effect.DebugApplied;
                if (!on)
                {
                    // Without the effect: the quads' centres as the text draws them (the effect's pass off, same measure).
                    effect.enabled = false;
                    Canvas.ForceUpdateCanvases();
                    var gen = t.cachedTextGenerator;
                    got = new List<float>();
                    var verts = gen.verts;
                    for (int q = 0; q + 3 < verts.Count; q += 4)
                        got.Add((verts[q].position.x + verts[q + 1].position.x + verts[q + 2].position.x + verts[q + 3].position.x) / 4 / t.pixelsPerUnit);
                }
                else if (!applied) got = new List<float>();
                return got;
            }
            bool Same(List<float> a, List<float> b) => a.Count > 0 && a.Count == b.Count && a.Zip(b, (x, y) => Mathf.Abs(x - y)).All(d => d < 0.6f);
            string Show(List<float> a) => string.Join(" ", a.Select(x => x.ToString("0.0")));
            var ones = Centers(counter, "1111", true);
            var eights = Centers(counter, "8888", true);
            yield return Shot("digits");
            var onesPlain = Centers(plain, "1111", false);
            var eightsPlain = Centers(plain, "8888", false);
            Report($"TabularDigits: \"1111\" and \"8888\" put their digits at the same places ({Show(ones)} / {Show(eights)}); without it they don't ({Show(onesPlain)} / {Show(eightsPlain)})",
                Same(ones, eights) && !Same(onesPlain, eightsPlain));
            var roll = new[] { "8000", "7999", "7350", "4123", "1111" }.Select(s => Centers(counter, s, true)).ToList();
            Report($"an LP roll (8000, 7999, 7350, 4123, 1111) keeps each digit's place ({string.Join(" | ", roll.Select(Show))})", roll.All(r => Same(r, roll[0])));
            var rich = Centers(counter, "<color=#FF8A8A>7,350</color>", true);
            var richPlain = Centers(counter, "8,000", true);
            Report($"with a colour tag and a comma, too ({Show(rich)} / {Show(richPlain)})", Same(rich, richPlain));
            // Centre and right alignment, and two lines.
            foreach (var anchor in new[] { TextAnchor.MiddleCenter, TextAnchor.MiddleRight })
            {
                counter.alignment = anchor;
                var a1 = Centers(counter, "1111", true);
                var a8 = Centers(counter, "8888", true);
                Report($"{anchor}: \"1111\" and \"8888\" at the same places ({Show(a1)} / {Show(a8)})", Same(a1, a8));
            }
            counter.alignment = TextAnchor.MiddleLeft;
            counter.horizontalOverflow = HorizontalWrapMode.Overflow;
            counter.verticalOverflow = VerticalWrapMode.Overflow;
            var two1 = Centers(counter, "1111\n8181", true);
            var two8 = Centers(counter, "8888\n1818", true);
            Report($"two lines: each line's digits keep their places ({Show(two1)} / {Show(two8)})", Same(two1, two8));
            // Mixed sizes: a <size> run's digits get that run's cell, not the text's.
            counter.alignment = TextAnchor.MiddleLeft;
            var mixed1 = Centers(counter, "11 <size=25>1111</size>", true);
            var mixed8 = Centers(counter, "88 <size=25>8888</size>", true);
            int smallPx = Mathf.RoundToInt(25 * counter.pixelsPerUnit);
            counter.font.RequestCharactersInTexture("0123456789", smallPx);
            float smallCell = "0123456789".Max(c => counter.font.GetCharacterInfo(c, out var ci, smallPx, FontStyle.Normal) ? ci.advance : 0) / counter.pixelsPerUnit;
            float step = mixed1.Count == 6 ? (mixed1[5] - mixed1[2]) / 3 : -1;
            Report($"mixed sizes: \"11 <size=25>1111</size>\" and its 8s at the same places ({Show(mixed1)} / {Show(mixed8)}); the small run steps {step:0.0} = its own cell {smallCell:0.0}",
                Same(mixed1, mixed8) && Mathf.Abs(step - smallCell) < 0.6f);
            Destroy(probeCanvas.gameObject);

            // ---- The screens: no faux weights, no built-in font ----
            var scanned = new List<Text>();
            IEnumerator Scan(string screen)
            {
                yield return null;
                Canvas.ForceUpdateCanvases();
                var texts = FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && t.text.Length > 0).ToList();
                scanned.AddRange(texts);
                var faux = texts.Where(t => t.fontStyle != FontStyle.Normal).Select(t => $"\"{Short(t.text)}\" ({t.fontStyle})").Distinct().ToList();
                var builtinTexts = texts.Where(t => ReferenceEquals(t.font, Typography.Builtin) || t.font == null).Select(t => $"\"{Short(t.text)}\"").Distinct().ToList();
                var others = texts.Where(t => t.font != null && Typography.FaceOfFont(t.font) == null && !ReferenceEquals(t.font, Typography.Builtin)).Select(t => $"\"{Short(t.text)}\" ({t.font.name})").Distinct().ToList();
                Report($"{screen}: {texts.Count} texts, none with a faux weight{(faux.Count > 0 ? ": " + string.Join(", ", faux.Take(6)) : "")}", faux.Count == 0);
                Report($"{screen}: none in the built-in font{(builtinTexts.Count > 0 ? ": " + string.Join(", ", builtinTexts.Take(6)) : "")}{(others.Count > 0 ? "; other fonts: " + string.Join(", ", others.Take(4)) : "")}",
                    builtinTexts.Count == 0 && others.Count == 0);
                var byFace = texts.GroupBy(t => t.font != null ? t.font.name : "none").Select(g => $"{g.Key} {g.Count()}");
                Note($"{screen}: faces in use: {string.Join(", ", byFace)}");
            }
            Report("the game opens on the title screen", _title.IsOpen);
            yield return Shot("title");
            yield return Scan("the title screen");
            _title.DebugPress();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("menu");
            yield return Scan("the main menu");
            _settings.Open();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("settings");
            yield return Scan("Settings");
            _settings.Close();
            _menu.DebugChoose(MainMenu.Choice.About);
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("about");
            yield return Scan("About");

            // ---- 7. The font licences ----
            var fontFiles = LicenseFiles.FindAll(LicenseFiles.Doc.Fonts);
            Report($"LicenseFiles finds both font licences ({string.Join(", ", fontFiles.Select(Path.GetFileName))} in {LicenseFiles.Folder})",
                LicenseFiles.Find(LicenseFiles.Doc.Fonts) != null && fontFiles.Count == 2);
            var about = string.Join("\n", _about.DebugTexts(false).Select(t => t.text));
            Report($"About credits the fonts (\"{AboutScreen.FontCredit}\")", about.Contains(AboutScreen.FontCredit));
            _about.ShowNotices();
            yield return new WaitForSecondsRealtime(0.4f);
            var reader = _about.DebugReader;
            int at = reader.text.IndexOf(AboutScreen.FontLicencesHeading);
            Report($"the third-party reader shows the font licences after the notices (at {at} of {reader.text.Length}; {reader.from})",
                at > reader.text.IndexOf("ygopro-core") && reader.text.LastIndexOf("Copyright 2017 The Barlow Project Authors") > at
                && reader.text.LastIndexOf("Copyright 2022 The Noto Project Authors") > at && reader.text.LastIndexOf("SIL OPEN FONT LICENSE Version 1.1") > at);
            yield return AboutScrollTo(at);
            yield return Shot("about-font-licences");
            _about.Back();
            _about.Back();

            // ---- The game's other screens: the campaign, its wallet, the shop, the van, the board and a dialog ----
            _menu.Close();
            _title.Close();
            _campaign.Start(CampaignState.Starters[1]);
            _campaignScreen.Open();
            yield return new WaitForSecondsRealtime(0.8f);
            yield return Shot("campaign");
            yield return Scan("the campaign screen");
            var (walletSize, walletWidest, walletRoom) = _campaignScreen.DebugLongWallet();
            yield return null;
            Canvas.ForceUpdateCanvases();
            (walletSize, walletWidest, walletRoom) = _campaignScreen.DebugLongWallet();
            yield return Shot("campaign-wallet");
            Report($"the wallet with large numbers (9,999,999 DP, 99,999 cards (12,345 different), 9,999 packs) draws inside its box with tabular digits: {walletWidest:0.0} of {walletRoom:0.0} at size {walletSize}",
                walletWidest > 0 && walletWidest <= walletRoom);
            _campaignScreen.Open(); // the real wallet back
            yield return new WaitForSecondsRealtime(0.3f);
            _campaignScreen.Close();
            _shop.Open();
            yield return new WaitForSecondsRealtime(0.6f);
            yield return Shot("shop");
            yield return Scan("the shop");
            _shop.Close();
            _van.DebugShow(0);
            yield return new WaitForSecondsRealtime(0.4f);
            _van.DebugPickShelf(0);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("van");
            yield return Scan("the van");
            var vanNames = FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && t.fontSize == 26 && t.resizeTextForBestFit && !t.supportRichText && t.text.Length > 0).ToList();
            Report($"the van's card name has its own text in the strong face ({string.Join(", ", vanNames.Select(t => $"\"{t.text}\" {t.font.name}"))})",
                vanNames.Count > 0 && vanNames.All(t => t.font == Typography.Font(Face.Strong)));
            _van.Hide();
            _shop.Close(); // (its Closed event opens the campaign screen)
            _campaignScreen.Close(); // the board must be the screen scanned
            UiTheme.Current = UiTheme.MainStreet;
            _pickCharacter[0] = -1;
            _pickDeck[0] = "classic";
            seed = 7;
            NewDuel();
            _paused = true;
            UpdateControls();
            yield return new WaitForSecondsRealtime(3f);
            Report($"the board is the screen on show (campaign {_campaignScreen.IsOpen}, shop {_shop.IsOpen}, van {_van.IsOpen})", !_campaignScreen.IsOpen && !_shop.IsOpen && !_van.IsOpen);
            yield return Shot("board");
            yield return Scan("the duel board");
            var heroes = FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && Typography.IsHero(t)).Select(t => $"{t.name}/{t.transform.parent?.name} \"{Short(t.text)}\"").ToList();
            Report($"hero numbers are marked where they're made ({heroes.Count}: {string.Join(", ", heroes.Take(8))})", heroes.Count >= 3);
            _view.OpenConcede();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Shot("dialog");
            yield return Scan("the concede dialog");
            _view.CloseConcede();

            // ---- 6. Faux bold through rich text: every script line with <b> or <i>, and those on the screens seen ----
            var fauxLines = corpusAsset != null ? corpusAsset.text.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("# faux ")).Select(l => l.Substring(7)).ToList() : new List<string>();
            Note($"script lines with <b> or <i> (faux weight on Barlow; a note until R2 ends): {fauxLines.Count}:\n    " + string.Join("\n    ", fauxLines));
            var tagged = scanned.Where(t => t.supportRichText && (t.text.Contains("<b>") || t.text.Contains("<i>"))).Select(t => $"\"{Short(t.text)}\"").Distinct().ToList();
            Note($"texts with <b> or <i> on the screens seen: {(tagged.Count == 0 ? "none" : string.Join(", ", tagged))}");
            Report($"the corpus lists the faux-weight lines ({fauxLines.Count})", fauxLines.Count > 0);

            Debug.Log($"type check: done, {bad} failed");
            if (bad == 0) Debug.Log("type check: ok");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        private static string Short(string s) => (s.Length > 40 ? s.Substring(0, 40) + "..." : s).Replace("\n", " ");

        /// <summary>Scrolls the About reader to about where character <paramref name="index"/> of its text sits.</summary>
        private IEnumerator AboutScrollTo(int index)
        {
            var reader = _about.DebugReader;
            var scroll = _about.DebugScroll;
            if (index <= 0 || reader.text.Length == 0 || scroll.room <= 0) yield break;
            _about.Page(0);
            float fraction = (float)index / reader.text.Length;
            // The reader pages by 90% of its height: page down until the heading's share of the text is reached.
            for (int i = 0; i < 400 && _about.DebugScroll.at < fraction * (scroll.room + 780) - 300 && _about.DebugScroll.at < scroll.room; i++) _about.Page(1);
            yield return null;
        }
    }
}
