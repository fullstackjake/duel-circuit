using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The no-pictures pass (docs/dev/PLAIN-CARDS-SPEC-2026-11-20.md, part S): a step the screen checks (-menucheck,
    /// -campaigncheck, -shopcheck, -themecheck, -layoutcheck) take at their shots. On a run started with no pictures
    /// (`-pics &lt;empty folder&gt; -hdpics off`) it waits for queued faces, captures the screen and reports FAIL for every face
    /// problem <see cref="CardFace.DebugAudit"/> finds, every tagged face that looks blank in the capture, and fewer tagged
    /// faces on the screen than expected. With pictures installed it does nothing, so those runs are unchanged.
    /// </summary>
    public sealed partial class DuelController
    {
        /// <summary>Whether this run has no card pictures (the audit's runs).</summary>
        private static bool NoPictures => CardView.Art != null && CardView.Art.Installed.Count == 0;

        private static int _auditsRun, _auditsSkipped;

        /// <summary>At a check's end: how many no-pictures audit steps ran and how many were skipped (pictures installed).</summary>
        private static void FaceAuditSummary(string check) =>
            Debug.Log($"{check} check: note {_auditsRun} no-pictures audit step{(_auditsRun == 1 ? "" : "s")} ran, {_auditsSkipped} skipped");

        /// <summary>The -pics folder when it was given and holds no files; else null.</summary>
        private static string? EmptyPicsFolder()
        {
            var args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-pics");
            if (at < 0 || at + 1 >= args.Length) return null;
            var folder = args[at + 1];
            return System.IO.Directory.Exists(folder) && System.IO.Directory.GetFiles(folder, "*", System.IO.SearchOption.AllDirectories).Length == 0 ? folder : null;
        }

        /// <summary>A screen's canvas by name ("TitleCanvas", "Canvas" for the board, "DeckEditorCanvas"...).</summary>
        private Transform? ScreenRoot(string canvas) => transform.Find(canvas);

        /// <summary>
        /// The shared step: the tagged faces under <paramref name="root"/> that show (inside any mask over them, not faded
        /// out), each with a texture and not blank, at least <paramref name="expected"/> of them, and whatever
        /// <paramref name="more"/> asks of them (null: fine; else what's wrong). Also given the expected count late
        /// (<paramref name="expectedOf"/>), for "every card on the screen"; <paramref name="only"/> keeps the faces that
        /// count (an opened pack's, not the dimmed shelf's behind it).
        /// </summary>
        private System.Collections.IEnumerator FaceAudit(Action<string, bool> report, string name, Transform? root, int expected,
            Func<List<FaceTag>, string?>? more = null, Func<int>? expectedOf = null, Func<FaceTag, bool>? only = null)
        {
            if (!NoPictures)
            {
                int installed = CardView.Art?.Installed.Count ?? 0;
                // Started with -pics naming an empty folder, yet pictures were found: the run isn't the one it claims to be.
                if (EmptyPicsFolder() is string folder)
                    report($"no pictures: {name}: started with -pics {folder} (empty), yet {installed} pictures are installed", false);
                else Debug.Log($"face audit: skipped \"{name}\": {installed} pictures installed");
                _auditsSkipped++;
                yield break;
            }
            _auditsRun++;
            // Faces asked for over a frame's budget show their kind's blank until they're drawn: wait for them.
            for (int i = 0; i < 600 && CardFace.DebugQueued > 0; i++) yield return null;
            if (CardFace.DebugQueued > 0) report($"no pictures: {name}: {CardFace.DebugQueued} faces still queued after 600 frames", false);
            yield return new WaitForEndOfFrame();
            if (root == null)
            {
                report($"faces on {name}: the screen isn't there", false);
                yield break;
            }
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            var (all, problems) = CardFace.DebugAudit(root);
            var faces = all.Where(f => Showing(f.transform) && (only == null || only(f))).ToList();
            var flat = faces.Where(f => CardFace.DebugFlat(shot, (RectTransform)f.transform)).ToList();
            if (expectedOf != null) expected = Math.Max(expected, expectedOf());
            var extra = more?.Invoke(faces);
            Destroy(shot);
            int plain = faces.Count(f => f.Plain);
            var layouts = string.Join(", ", faces.GroupBy(f => f.Layout).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
            report($"no pictures: {name} shows {faces.Count} card faces (at least {expected}: {layouts}), {plain} plain, {flat.Count} blank, "
                   + $"{problems.Count} problem{(problems.Count == 1 ? "" : "s")}{(extra != null ? "; " + extra : "")}",
                faces.Count >= expected && flat.Count == 0 && problems.Count == 0 && extra == null);
            foreach (var p in problems.Take(8)) report($"no pictures: {name}: {p}", false);
            if (problems.Count > 8) report($"no pictures: {name}: and {problems.Count - 8} more problems", false);
            foreach (var f in flat.Take(8)) report($"no pictures: {name}: the face of card {f.Code} ({PathOf(f.transform, root)}) is blank in the capture", false);
            if (flat.Count > 8) report($"no pictures: {name}: and {flat.Count - 8} more blank faces", false);
        }

        /// <summary>For "every card": the card views under <paramref name="root"/> that draw a face and show.</summary>
        private static int CardsShowing(Transform? root) =>
            root == null ? 0 : root.GetComponentsInChildren<CardView>(false).Count(v => v.DebugFaceDrawn && Showing(v.transform));

        /// <summary>For "a rarity mark on each Rare and up": the marks the faces wear ("R", "SR"...), sorted.</summary>
        private static List<string> RarityMarks(IEnumerable<FaceTag> faces) =>
            faces.Select(f => f.transform.Find("RarityMark")).Where(m => m != null && m.gameObject.activeInHierarchy)
                .Select(m => m!.GetComponentInChildren<Text>()?.text ?? "").OrderBy(s => s, StringComparer.Ordinal).ToList();

        /// <summary>A face in the shop's opened pack (its cards sit in slots named "Slot").</summary>
        private static bool InPackSlot(FaceTag face)
        {
            for (var p = face.transform.parent; p != null; p = p.parent)
                if (p.name == "Slot") return true;
            return false;
        }

        /// <summary>For an opened pack: at least a pack's cards in its slots, and their rarity marks as <paramref name="ok"/> wants.</summary>
        private static string? PackMarks(List<FaceTag> faces, Func<List<string>, bool> ok, string wanted)
        {
            var inPack = faces.Where(InPackSlot).ToList();
            if (inPack.Count < PackCatalog.PackSize) return $"{inPack.Count} faces in the pack's slots";
            var marks = RarityMarks(inPack);
            return ok(marks) ? null : $"rarity marks \"{string.Join(" ", marks)}\", wanted {wanted}";
        }

        /// <summary>
        /// For an opened pack: the marks its Rare-and-up cards should wear, read from each slot's rarity caption (the shop's
        /// "Rarity" line under the card), sorted.
        /// </summary>
        private static List<string> CaptionMarks(List<FaceTag> faces)
        {
            var marks = new List<string>();
            foreach (var face in faces.Where(InPackSlot))
            {
                var slot = face.transform.parent;
                while (slot != null && slot.name != "Slot") slot = slot.parent;
                var caption = slot?.Find("Rarity")?.GetComponent<Text>();
                var said = caption == null ? "" : System.Text.RegularExpressions.Regex.Replace(caption.text, "<[^>]+>", "").Trim();
                foreach (Ygo.Engine.Data.Rarity r in Enum.GetValues(typeof(Ygo.Engine.Data.Rarity)))
                    if (Ygo.Engine.Data.Rarities.Name(r) == said && r >= Ygo.Engine.Data.Rarity.Rare) marks.Add(CardFace.RarityShort(r));
            }
            marks.Sort(StringComparer.Ordinal);
            return marks;
        }

        /// <summary>Whether a face is on screen and visible: not clipped out by a mask over it, not faded out.</summary>
        private static bool Showing(Transform t)
        {
            var rt = (RectTransform)t;
            var box = Box(rt);
            if (box.width < 2 || box.height < 2 || box.xMax <= 0 || box.yMax <= 0 || box.xMin >= Screen.width || box.yMin >= Screen.height) return false;
            if (t.TryGetComponent<Graphic>(out var g) && g.color.a < 0.5f) return false;
            float alpha = 1;
            for (var p = t; p != null; p = p.parent)
            {
                if (p.TryGetComponent<CanvasGroup>(out var group)) alpha *= group.alpha;
                bool masks = (p.TryGetComponent<RectMask2D>(out var m2) && m2.enabled) || (p.TryGetComponent<Mask>(out var m) && m.enabled);
                if (masks && p != t && !Box((RectTransform)p).Contains(box.center)) return false;
            }
            return alpha >= 0.5f;
        }

        private static Rect Box(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners); // overlay canvases: world is screen pixels
            float x0 = corners.Min(c => c.x), y0 = corners.Min(c => c.y), x1 = corners.Max(c => c.x), y1 = corners.Max(c => c.y);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        /// <summary>The first object named <paramref name="name"/> under <paramref name="root"/> (depth first), or null.</summary>
        private static Transform? FindDeep(Transform? root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name && t != root) return t;
            return null;
        }

        private static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var p = t; p != null && p != root; p = p.parent) parts.Add(p.name);
            parts.Reverse();
            return string.Join("/", parts.TakeLast(4));
        }
    }
}
