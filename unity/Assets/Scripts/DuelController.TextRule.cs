using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The layout rule (docs/dev/TYPE-AND-FONTS-SPEC-2026-11-21.md, sections 5 and 7), taken at the shots of -layoutcheck,
    /// -campaigncheck, -menucheck, -shopcheck, -themecheck, -alleycheck, -tagcheck, -aboutcheck, -editorcheck, -searchcheck,
    /// -focuscheck and -replaycheck's list:
    /// every visible shell text draws at <see cref="UiTheme.Floor"/> or more at 1080 (best-fit text at the size it came out,
    /// scaled parents and &lt;size&gt; runs counted), and none is clipped: it loses no characters to its box (vertical
    /// truncation; best fit at its minimum and still over), no single word is wider than its box, and an overflowing text
    /// runs past its box by no more than its leading. Text under a screen drawn over the whole window doesn't count. Hero
    /// numbers (<see cref="Typography.IsHero"/>) keep their sizes. Text drawn on a card or a slab (<see cref="CardText"/>) is
    /// judged in screen pixels against the card's floor (section 2: 7 px; plain faces themselves, drawn into textures, are
    /// -plaincheck's). The areas in <see cref="TextRuleExclusions"/> are skipped by name.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Areas the rule skips, each named in the log: the developer layout. (A rarity mark is card text, CardText
        /// at 7 px, judged like any other.)</summary>
        private static readonly (string what, Func<Text, bool> test)[] TextRuleExclusions =
        {
            // Not R3's: the board in its developer layout (F12; only with -dev, the editor, a dev build or a check) is a tool,
            // drawn at 0.6 scale beside the dev panel, not a player's screen.
            ("the board in the developer layout (F12, not a player's screen)", t => _ruleDevBoard != null && t.transform.IsChildOf(_ruleDevBoard)),
        };

        /// <summary>The board's canvas while it shows the developer layout (else null), for the exclusion above.</summary>
        private static Transform? _ruleDevBoard;

        private static bool _textRuleNamed;


        /// <summary>A text the rule found under the floor or clipped: the Text itself (the re-check matches on it) and the note.</summary>
        internal sealed class TextProblem
        {
            public Text Text = null!;
            public bool Small;
            public string Note = "";
        }

        /// <summary>
        /// One line per shot: "text rule on &lt;shot&gt;: ..." ok, or FAIL with each text under the floor or clipped
        /// (the full list in note lines). A problem must still be there 0.4 s later on the same Text (a badge popping in, a
        /// banner growing are on their way to their size; a log or a countdown whose words change stays caught); the
        /// screenshot is the shot's frame. The first call also names the exclusions and runs the self-test.
        /// </summary>
        private System.Collections.IEnumerator TextRule(string shot, Action<string, bool> report)
        {
            var board = ScreenRoot("Canvas");
            _ruleDevBoard = board != null && board.gameObject.activeInHierarchy && _view != null && !_view.PlayLayout ? board : null;
            if (!_textRuleNamed)
            {
                _textRuleNamed = true;
                Debug.Log($"text rule: excluded: {string.Join("; ", TextRuleExclusions.Select(e => e.what))}");
                SelfTest(report);
            }
            var (problems, seen) = TextProblems();
            if (problems.Count > 0)
            {
                yield return new WaitForSecondsRealtime(0.4f);
                var (again, seen1) = TextProblems();
                problems = StillThere(problems, again);
                seen = seen1;
            }
            int small = problems.Count(p => p.Small), clipped = problems.Count - small;
            var lines = problems.Select(p => (p.Small ? $"under {UiTheme.Floor}: " : "clipped: ") + p.Note).ToList();
            report($"text rule on {shot}: {seen} texts, {small} under {UiTheme.Floor} at 1080, {clipped} clipped"
                   + (lines.Count > 0 ? ": " + string.Join("; ", lines.Take(4)) + (lines.Count > 4 ? $"; and {lines.Count - 4} more" : "") : ""),
                lines.Count == 0);
            foreach (var line in lines) Debug.Log($"text rule: note   {shot}: {line}");
            if (_sidewaysChecked.Count > 0) Debug.Log($"text rule: seen   {shot}: {_sidewaysChecked.Count} one-line texts checked for a sideways spill: {string.Join(", ", _sidewaysChecked.Take(30))}");
        }

        /// <summary>The second look's problems that the first look also had, on the same Text object and of the same kind.</summary>
        internal static List<TextProblem> StillThere(List<TextProblem> first, List<TextProblem> second) =>
            second.Where(p => p.Text != null && first.Any(f => f.Text == p.Text && f.Small == p.Small)).ToList();

        /// <summary>A visible text of this look: its Text and its ink's rect on the screen.</summary>
        private readonly struct Seen
        {
            public readonly Text Text;
            public readonly Rect Ink;
            public Seen(Text text, Rect ink) { Text = text; Ink = ink; }
        }

        /// <summary>The visible shell texts under the floor and the clipped ones, and how many were looked at.</summary>
        internal static (List<TextProblem> problems, int seen) TextProblems()
        {
            var problems = new List<TextProblem>();
            Canvas.ForceUpdateCanvases(); // so best-fit text reports the size it came out at this frame
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var corners = new Vector3[4];
            int covered = CoveredBelow(screen, corners);
            var shown = new List<Seen>();
            foreach (var text in FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text)) continue;
                if (Alpha(text) < 0.1f || Typography.IsHero(text)) continue;
                if (TextRuleExclusions.Any(e => e.test(text))) continue;
                var canvas = text.canvas != null ? text.canvas.rootCanvas : null;
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                if (canvas.sortingOrder < covered) continue; // under a screen drawn over the whole window
                var box = WorldRect(text.rectTransform, text.rectTransform.rect, corners);
                if (!box.Overlaps(screen) || box.width < 1 || box.height < 1) continue; // off the screen (a closed drawer)
                shown.Add(new Seen(text, WorldRect(text.rectTransform, InkRect(text), corners)));
            }
            // What a sideways spill may not run into: another text's ink, or a button the text isn't part of.
            var buttons = FindObjectsByType<Selectable>(FindObjectsSortMode.None)
                .Where(b => b.isActiveAndEnabled && b.transform is RectTransform && Alpha01(b.transform) >= 0.1f)
                .Select(b => (b.transform, rect: WorldRect((RectTransform)b.transform, ((RectTransform)b.transform).rect, corners))).ToList();
            _sidewaysChecked.Clear();
            foreach (var s in shown)
            {
                var text = s.Text;
                var rt = text.rectTransform;
                var canvas = text.canvas.rootCanvas;
                float ppu = text.pixelsPerUnit;
                // The text's own scale against its canvas (a scaled panel draws smaller than its font size says).
                float rel = canvas.transform.lossyScale.y > 0 ? rt.lossyScale.y / canvas.transform.lossyScale.y : 1;
                var gen = text.cachedTextGenerator;
                bool fit = text.resizeTextForBestFit && gen.fontSizeUsedForBestFit > 0;
                // fontSizeUsedForBestFit is in pixels (the generator works at fontSize × pixelsPerUnit).
                float px = (fit ? gen.fontSizeUsedForBestFit : text.fontSize * ppu) * rel;
                float at1080 = px / ppu;
                // Best fit picks whole pixels: a minimum of 14 at 720 draws at 9 px; that is the floor there.
                // Text drawn on a card or a slab (CardText) has the card's floor in screen pixels (section 2: 7 px, 8 for card
                // text); shell text the type scale's floor at 1080.
                bool onCard = text.TryGetComponent<CardText>(out var cardText);
                bool under = onCard ? px < cardText.FloorPx - 0.01f
                    : fit ? px < Mathf.FloorToInt(UiTheme.Floor * ppu * rel) - 0.01f : text.fontSize * rel < UiTheme.Floor - 0.01f;
                string where = $"{(rt.parent != null ? rt.parent.name + "/" : "")}{rt.name}: \"{Words(text.text)}\"";
                if (under) problems.Add(new TextProblem { Text = text, Small = true, Note = onCard ? $"{where} ({px:0.#} px on screen, a card's floor is {cardText.FloorPx:0} px)"
                    : $"{where} ({at1080:0.#} at 1080{(fit ? $", best fit {text.resizeTextMinSize}-{text.resizeTextMaxSize}" : "")})" });
                // A rich-text run with a smaller <size=N> than the floor (a second line in one text), best fit or not.
                else if (text.supportRichText && text.text.Contains("<size="))
                {
                    int least = int.MaxValue;
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text.text, @"<size=(\d+)>"))
                        least = Math.Min(least, int.Parse(m.Groups[1].Value));
                    if (!onCard && least * rel < UiTheme.Floor - 0.01f) problems.Add(new TextProblem { Text = text, Small = true, Note = $"{where} (a <size={least}> run: {least * rel:0.#} at 1080)" });
                }
                var why = Clip(text) ?? Sideways(s, shown, buttons, screen);
                if (why != null) problems.Add(new TextProblem { Text = text, Note = $"{where} ({why})" });
            }
            return (problems, shown.Count);
        }

        /// <summary>A local rect of <paramref name="rt"/> on the screen.</summary>
        private static Rect WorldRect(RectTransform rt, Rect local, Vector3[] corners)
        {
            var a = rt.TransformPoint(new Vector3(local.xMin, local.yMin));
            var b = rt.TransformPoint(new Vector3(local.xMax, local.yMax));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>
        /// Where a text's ink is, in its local units: its preferred size placed by its alignment in its box; an overflowing
        /// axis keeps the whole preferred size (it runs past the box), a wrapping or truncating one stays inside it.
        /// </summary>
        private static Rect InkRect(Text text)
        {
            var box = text.rectTransform.rect;
            var settings = text.GetGenerationSettings(new Vector2(text.horizontalOverflow == HorizontalWrapMode.Wrap ? box.width : 0, 0));
            settings.resizeTextForBestFit = false;
            if (text.resizeTextForBestFit && text.cachedTextGenerator.fontSizeUsedForBestFit > 0)
                settings.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.cachedTextGenerator.fontSizeUsedForBestFit / text.pixelsPerUnit));
            float w = OpenGen.GetPreferredWidth(text.text, settings) / text.pixelsPerUnit;
            float h = OpenGen.GetPreferredHeight(text.text, settings) / text.pixelsPerUnit;
            if (text.horizontalOverflow == HorizontalWrapMode.Wrap) w = Mathf.Min(w, box.width);
            if (text.verticalOverflow == VerticalWrapMode.Truncate) h = Mathf.Min(h, box.height);
            int a = (int)text.alignment;
            float x = (a % 3) switch { 0 => box.xMin, 1 => box.center.x - w / 2, _ => box.xMax - w };
            float y = (a / 3) switch { 0 => box.yMax - h, 1 => box.center.y - h / 2, _ => box.yMin };
            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// For a text that doesn't wrap: why its line running past its box's sides is a problem (it runs off the screen, or
        /// under or over another text or a button it isn't part of), or null.
        /// </summary>
        private static string? Sideways(Seen s, List<Seen> shown, List<(Transform transform, Rect rect)> buttons, Rect screen)
        {
            var text = s.Text;
            if (text.horizontalOverflow != HorizontalWrapMode.Overflow) return null;
            _sidewaysChecked.Add($"{(text.transform.parent != null ? text.transform.parent.name + "/" : "")}{text.name} \"{Words(text.text)}\"");
            var corners = new Vector3[4];
            var box = WorldRect(text.rectTransform, text.rectTransform.rect, corners);
            if (s.Ink.xMin >= box.xMin - 0.5f && s.Ink.xMax <= box.xMax + 0.5f) return null; // inside its box
            // Only the part past the box's sides can run into something.
            var parts = new List<Rect>();
            if (s.Ink.xMin < box.xMin - 0.5f) parts.Add(Rect.MinMaxRect(s.Ink.xMin, s.Ink.yMin, box.xMin, s.Ink.yMax));
            if (s.Ink.xMax > box.xMax + 0.5f) parts.Add(Rect.MinMaxRect(box.xMax, s.Ink.yMin, s.Ink.xMax, s.Ink.yMax));
            float onePx = 1;
            foreach (var part in parts)
            {
                if (part.xMin < screen.xMin - onePx || part.xMax > screen.xMax + onePx) return $"its line runs {part.width:0} px past its box and off the screen";
                foreach (var other in shown)
                {
                    if (other.Text == text || other.Text.transform.IsChildOf(text.transform) || text.transform.IsChildOf(other.Text.transform)) continue;
                    if (Overlap(part, other.Ink) > onePx) return $"its line runs {part.width:0} px past its box, into \"{Words(other.Text.text)}\"";
                }
                foreach (var (b, rect) in buttons)
                    if (!text.transform.IsChildOf(b) && Overlap(part, rect) > onePx) return $"its line runs {part.width:0} px past its box, under the button {b.name}";
            }
            return null;
        }

        /// <summary>The texts the last look checked for a sideways spill (they don't wrap), for the log.</summary>
        private static readonly List<string> _sidewaysChecked = new List<string>();

        /// <summary>The smaller side of two rects' overlap (0: they don't touch).</summary>
        private static float Overlap(Rect a, Rect b) =>
            Mathf.Max(0, Mathf.Min(Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin)));

        private static float Alpha01(Transform t) => t.TryGetComponent<Graphic>(out var g) ? g.color.a * GroupAlpha(t) : GroupAlpha(t);

        /// <summary>
        /// The rule can fail. On a probe canvas: a 13 text, a best-fit text shrunk under 14, a 20 text under a 0.5 scale and a
        /// &lt;size=10&gt; run in a best-fit text are under the floor; a long line in a one-line box, a best fit at its minimum
        /// still over, a too-wide word, lines spilling out of an overflowing box and a one-line text running sideways into
        /// its neighbour are clipped; a 14 text, a line cut with "…", leading-only overflow, a hero number, a faded text and
        /// a text under an excluded name pass; a text under a full-window opaque screen is skipped; the re-check keeps a
        /// problem whose words changed on the same Text.
        /// </summary>
        private static void SelfTest(Action<string, bool> report)
        {
            var root = Ui.Overlay(null!, "TextRuleProbe", 90);
            var canvasGo = root.parent.gameObject;
            GameObject? coverGo = null;
            try
            {
                Text Probe(string name, string s, int size, float w, float h, bool fit = false, int min = 0, Transform? parent = null, float x = 10, float y = 10)
                {
                    var t = Ui.Text(Ui.Box(parent ?? root, x, y, w, h), s, size, Color.white);
                    t.name = name;
                    if (fit) { t.resizeTextForBestFit = true; t.resizeTextMinSize = min; t.resizeTextMaxSize = size; }
                    return t;
                }
                var small = Probe("Small", "Thirteen", 13, 300, 40);
                Canvas.ForceUpdateCanvases();
                var shrunk = Probe("Shrunk", "A best-fit line that has to shrink to fit", 20, 120, 24, fit: true, min: 10);
                Probe("Cut", "A line far too long for its box, which holds one line only", 18, 120, 24);
                Probe("Over", "A best-fit line at its minimum and still over its box", 20, 60, 20, fit: true, min: 16);
                Probe("Word", "Polymerization", 18, 40, 80);
                Probe("Fits", "Fourteen", 14, 300, 40);
                Probe("Ellipsis", "Cut by design…", 14, 300, 40);
                Probe("Spill", "Three lines of text that overflow a box one line high", 16, 140, 20).verticalOverflow = VerticalWrapMode.Overflow;
                Probe("Leading", "One line", 14, 300, 15).verticalOverflow = VerticalWrapMode.Overflow;
                var half = Ui.Box(root, 400, 300, 400, 100);
                half.localScale = Vector3.one * 0.5f;
                Probe("Nested", "Twenty at half scale", 20, 300, 40, parent: half);
                var run = Probe("Run", "Fourteen <size=10>and ten</size>", 14, 400, 40, fit: true, min: 14, x: 10, y: 400);
                run.supportRichText = true;
                Typography.Hero(Probe("Hero", "13", 13, 100, 40, x: 10, y: 460));
                Probe("Faded", "Thirteen", 13, 300, 40, x: 10, y: 520).color = new Color(1, 1, 1, 0.05f);
                // The excluded area: a 13 text on a stand-in for the developer layout's board.
                var devStandIn = Ui.Box(root, 0, 0, 1920, 1080);
                Probe("Excluded", "Thirteen", 13, 300, 40, parent: devStandIn.gameObject.transform, x: 10, y: 580);
                var devBefore = _ruleDevBoard;
                _ruleDevBoard = devStandIn;
                var sideways = Probe("Sideways", "A one-line label far wider than its little box", 16, 80, 30, x: 1000, y: 700);
                sideways.horizontalOverflow = HorizontalWrapMode.Overflow;
                sideways.alignment = TextAnchor.MiddleLeft;
                Probe("Neighbour", "Next door", 16, 200, 30, x: 1100, y: 700);
                // Card text: judged in screen pixels against 7 px (a 6 px name fails, a 7 px one passes), whatever its size at 1080.
                float ppuNow = small.pixelsPerUnit;
                Probe("CardSix", "Name", Mathf.Max(1, Mathf.FloorToInt(6f / ppuNow)), 200, 40, x: 10, y: 640).gameObject.AddComponent<CardText>();
                Probe("CardSeven", "Name", Mathf.CeilToInt(7f / ppuNow), 200, 40, x: 300, y: 640).gameObject.AddComponent<CardText>();
                var (first, _) = TextProblems();
                _ruleDevBoard = devBefore;
                bool Has(List<TextProblem> list, string name, bool isSmall) => list.Any(p => p.Text != null && p.Text.name == name && p.Small == isSmall);
                bool caught = Has(first, "Small", true) && Has(first, "Shrunk", true) && Has(first, "Nested", true) && Has(first, "Run", true)
                    && Has(first, "Cut", false) && Has(first, "Over", false) && Has(first, "Word", false) && Has(first, "Spill", false) && Has(first, "Sideways", false) && Has(first, "CardSix", true);
                string[] spare = { "Fits", "Ellipsis", "Leading", "Hero", "Faded", "Excluded", "Neighbour", "CardSeven" };
                var wrongly = first.Where(p => p.Text != null && spare.Contains(p.Text.name)).Select(p => p.Note).ToList();
                // The re-check: the same Text with other words is still the same problem.
                small.text = "Thirteen, now other words";
                var (second, _) = TextProblems();
                bool recheck = StillThere(first, second).Any(p => p.Text == small);
                // Occlusion: under a full-window opaque screen (a plain colour, no sprite) the probes don't count; a sprite doesn't hide them.
                var cover = Ui.Overlay(null!, "TextRuleCover", 91);
                coverGo = cover.parent.gameObject;
                var coverImage = Ui.Image(cover, Color.black);
                var (underCover, _) = TextProblems();
                bool hidden = !underCover.Any(p => p.Text == small);
                coverImage.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero);
                var (underSprite, _) = TextProblems();
                bool spriteShows = underSprite.Any(p => p.Text == small);
                string calibration = $"the shrunk probe: best fit {shrunk.cachedTextGenerator.fontSizeUsedForBestFit} px at {shrunk.pixelsPerUnit:0.###} px per unit";
                var mine = first.Where(p => p.Text != null && p.Text.transform.IsChildOf(root)).Select(p => p.Note);
                report("text rule: the rule catches a 13 text, best fit shrunk under 14, a 20 text at half scale, a <size=10> run in a best-fit text, a cut line, "
                       + "best fit at its minimum still over, a word wider than its box, lines spilling out, a line running sideways into its neighbour, a 6 px card name; "
                       + "spares a 14 text, a line cut with \"…\", leading-only overflow, a hero number, a faded text, an excluded area, a 7 px card name; skips text under a full-window "
                       + $"opaque screen but not under a sprite; keeps a problem whose words change (caught {caught}, wrongly caught {wrongly.Count}, re-check {recheck}, "
                       + $"covered {hidden}, sprite {spriteShows}; {string.Join(" / ", mine)}; {calibration})",
                    caught && wrongly.Count == 0 && recheck && hidden && spriteShows);
            }
            finally
            {
                if (coverGo != null) DestroyImmediate(coverGo);
                DestroyImmediate(canvasGo);
            }
        }

        /// <summary>
        /// The sorting order under which nothing shows: the highest root canvas with an opaque image over the whole window
        /// (a screen's background over the board behind it). int.MinValue when none.
        /// </summary>
        private static int CoveredBelow(Rect screen, Vector3[] corners)
        {
            int covered = int.MinValue;
            foreach (var image in FindObjectsByType<Image>(FindObjectsSortMode.None))
            {
                if (!image.isActiveAndEnabled || image.canvas == null) continue;
                var root = image.canvas.rootCanvas;
                if (!root.isActiveAndEnabled || root.sortingOrder <= covered) continue;
                // Opaque only: a plain colour (no sprite) nearly fully opaque; a sprite may have see-through parts.
                if (image.sprite != null || image.color.a * GroupAlpha(image.transform) < 0.95f) continue;
                image.rectTransform.GetWorldCorners(corners);
                // The whole width and most of the height: a window taller than 16:9 letterboxes the layout (bars above and below).
                float h = Mathf.Min(corners[2].y, screen.yMax) - Mathf.Max(corners[0].y, screen.yMin);
                if (corners[0].x <= screen.xMin + 1 && corners[2].x >= screen.xMax - 1 && h >= screen.height * 0.8f)
                    covered = root.sortingOrder;
            }
            return covered;
        }

        private static float GroupAlpha(Transform t)
        {
            float a = 1;
            for (var p = t; p != null; p = p.parent)
                if (p.TryGetComponent<CanvasGroup>(out var g) && g.enabled) { a *= g.alpha; if (g.ignoreParentGroups) break; }
            return a;
        }

        /// <summary>The text's alpha with its canvas groups' (a faded-out panel doesn't count).</summary>
        private static float Alpha(Text text)
        {
            float a = text.color.a;
            for (var p = text.transform; p != null; p = p.parent)
                if (p.TryGetComponent<CanvasGroup>(out var g) && g.enabled) { a *= g.alpha; if (g.ignoreParentGroups) break; }
            return a;
        }

        private static string Words(string s)
        {
            var words = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "").Replace('\n', ' ');
            return words.Length > 48 ? words.Substring(0, 48) + "…" : words;
        }

        private static readonly TextGenerator ClipGen = new TextGenerator(), OpenGen = new TextGenerator();

        /// <summary>
        /// Why the text is clipped, or null: characters its box drops (vertical truncation, best fit included: it shrinks
        /// first, so a loss means it is at its minimum and still over), or a single word wider than its wrapping box.
        /// Text that overflows on purpose (Overflow modes) can't lose characters, so only the word rule applies to it.
        /// </summary>
        private static string? Clip(Text text)
        {
            var rt = text.rectTransform;
            var size = rt.rect.size;
            if (size.x <= 0 || size.y <= 0) return null;
            float ppu = text.pixelsPerUnit;
            var settings = text.GetGenerationSettings(size);
            if (text.verticalOverflow == VerticalWrapMode.Truncate)
            {
                ClipGen.Populate(text.text, settings);
                var open = settings;
                open.verticalOverflow = VerticalWrapMode.Overflow;
                OpenGen.Populate(text.text, open);
                int shown = Visible(ClipGen), all = Visible(OpenGen);
                if (shown < all) return $"{all - shown} of {all} characters cut by its {size.x:0}x{size.y:0} box";
            }
            else
            {
                // Overflowing on purpose may only mean a line's leading past a tight box, not a line that spills out of it.
                var open = settings;
                open.resizeTextForBestFit = false;
                if (text.resizeTextForBestFit && text.cachedTextGenerator.fontSizeUsedForBestFit > 0)
                    open.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.cachedTextGenerator.fontSizeUsedForBestFit / ppu));
                float h = OpenGen.GetPreferredHeight(text.text, open) / ppu;
                float slack = 0.35f * open.fontSize;
                if (h > size.y + slack) return $"its lines run {h - size.y:0} past its {size.x:0}x{size.y:0} box";
            }
            if (text.horizontalOverflow == HorizontalWrapMode.Wrap)
            {
                var one = settings;
                one.horizontalOverflow = HorizontalWrapMode.Overflow;
                one.verticalOverflow = VerticalWrapMode.Overflow;
                one.resizeTextForBestFit = false;
                if (text.resizeTextForBestFit && text.cachedTextGenerator.fontSizeUsedForBestFit > 0)
                    one.fontSize = text.cachedTextGenerator.fontSizeUsedForBestFit; // already in pixels: scale 1 below
                else one.fontSize = Mathf.RoundToInt(text.fontSize * ppu);
                one.scaleFactor = 1;
                var plain = System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "");
                foreach (var word in plain.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // A rule of "────" in a licence text, or a web address, breaks anywhere by design.
                    if (!word.Any(char.IsLetterOrDigit) || word.Contains("://")) continue;
                    float w = OpenGen.GetPreferredWidth(word, one) / ppu;
                    if (w > size.x + 0.5f) return $"the word \"{word}\" is {w:0} wide in a {size.x:0} box";
                }
            }
            return null;
        }

        /// <summary>Characters the generator placed that draw something (rich-text tags and spaces aside).</summary>
        private static int Visible(TextGenerator gen)
        {
            int n = 0;
            var chars = gen.characters;
            for (int i = 0; i < chars.Count; i++) if (chars[i].charWidth > 0) n++;
            return n;
        }
    }
}
