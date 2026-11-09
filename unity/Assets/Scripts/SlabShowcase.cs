using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The slab sheets for <see cref="FoilShowcase"/> (`-foilslabs [panel|labels|context]`) and the reveal's frames
    /// (`-slabreveal &lt;folder&gt;`), all under the fixed light, so two runs give the same pictures.
    /// <list type="bullet">
    /// <item>panel: Full, Reveal and Compact for a Gem Mint Secret Rare, a Mint Ultra Rare and an Excellent Common spell.</item>
    /// <item>labels: grades 10, 9, 8, 6, 4, 2 and a sealed label (rows) at Full, Full at 0.62, Reveal and Compact (columns),
    /// each slab cropped to its label, on the installed card with the longest name; logs "slabfit: &lt;size&gt; &lt;grade&gt;
    /// &lt;child&gt; ok|FAIL" for every label text (tools/foil_shots.sh fails on a FAIL).</item>
    /// <item>context: Full at 0.62 on the panel colour, Full on the reveal's scrim, a Full on light parchment, and grid rims at
    /// the deck editor's, the van's, the desk's and the shop's tile sizes on their screens' backgrounds.</item>
    /// </list>
    /// </summary>
    public sealed class SlabShowcase : MonoBehaviour
    {
        private static readonly List<(string column, string grade, RectTransform slab)> Checked = new List<(string, string, RectTransform)>();

        public static void Lay(RectTransform root, ICardDataProvider cards, string mode)
        {
            CardWear.Override = CardWear.Display.Full;
            CardWear.PrepareNow();
            if (mode == "labels") LayLabels(root, cards);
            else if (mode == "context") LayContext(root, cards);
            else LayPanel(root, cards);
        }

        private static CardCopy Copy(uint code, Rarity rarity, int grade) =>
            new CardCopy { Code = code, Rarity = rarity, Grade = grade, Slab = grade, Seed = Hash.Mix(code, (uint)grade) };

        private static string SetCode(CardInfo info, Rarity rarity)
        {
            var printings = CardFoil.Printings?.Of(info.Code, info.Alias);
            if (printings == null || printings.Length == 0) return "";
            var match = printings.FirstOrDefault(p => p.Rarity == rarity);
            return match.SetCode ?? printings[0].SetCode ?? "";
        }

        /// <summary>
        /// Three graded copies in slabs, each at Full with Reveal and Compact beside it: a Gem Mint Secret Rare, a Mint
        /// Ultra Rare and an Excellent Common spell (wear on, whatever the setting).
        /// </summary>
        private static void LayPanel(RectTransform root, ICardDataProvider cards)
        {
            var copies = new (uint code, Rarity rarity, int grade)[]
            {
                (44508094, Rarity.SecretRare, 10), // Stardust Dragon
                (46986414, Rarity.UltraRare, 9),   // Dark Magician
                (55144522, Rarity.Common, 6),      // Pot of Greed
            };
            for (int i = 0; i < copies.Length; i++)
            {
                var (code, rarity, grade) = copies[i];
                var info = cards.Get(code);
                if (info == null) continue;
                var copy = Copy(code, rarity, grade);
                var set = SetCode(info, rarity);
                // A group 564 wide: Full (360x586) on the left; Reveal (184x268) top right, Compact (121x176) under it.
                float left = (i - 1) * 610 - 282, top = 30 + 293;
                SlabView.Create(root, new Vector2(left + 180, top - 293), SlabView.Size.Full, copy, info, set);
                SlabView.Create(root, new Vector2(left + 380 + 92, top - 134), SlabView.Size.Reveal, copy, info, set);
                SlabView.Create(root, new Vector2(left + 380 + 92, top - 268 - 24 - 88), SlabView.Size.Compact, copy, info, set);
                var label = Ui.Text(Ui.Placed("Caption", root, new Vector2(left + 282, top - 586 - 34), new Vector2(560, 44)),
                    $"{CardFoil.ColoredName(rarity)} · slab {grade}\n<size=12>{info.Name} · Full, Reveal, Compact</size>", 17, Color.white);
                label.supportRichText = true;
            }
        }

        /// <summary>Every label at every size, cropped round the label (see the class comment).</summary>
        private static void LayLabels(RectTransform root, ICardDataProvider cards)
        {
            Checked.Clear();
            var info = LongestName(cards);
            if (info == null) return;
            var set = SetCode(info, Rarity.UltraRare);
            // Columns: size, scale, the crop's height and the slab y (case px) it centres on.
            var columns = new (string name, SlabView.Size size, float scale, float height, float focus)[]
            {
                ("Full", SlabView.Size.Full, 1, 104, -245),
                ("Full62", SlabView.Size.Full, 0.62f, 66, -245),
                ("Reveal", SlabView.Size.Reveal, 1, 52, -108),
                ("Compact", SlabView.Size.Compact, 1, 40, -71),
            };
            var widths = columns.Select(c => (c.size == SlabView.Size.Full ? 360 : c.size == SlabView.Size.Reveal ? 184 : 121) * c.scale + 24).ToArray();
            float gap = 40, total = widths.Sum() + gap * (widths.Length - 1), x0 = -total / 2 + 60;
            var grades = new[] { 10, 9, 8, 6, 4, 2, 0 };
            Caption(root, new Vector2(0, 490), $"Label fit: {info.Name} ({set}) · rows 10, 9, 8, 6, 4, 2, sealed · Full, Full at 0.62, Reveal, Compact", 15);
            for (int c = 0, x = 0; c < columns.Length; x += (int)(widths[c] + gap), c++)
            {
                var col = columns[c];
                float cx = x0 + x + widths[c] / 2;
                Caption(root, new Vector2(cx, 450), col.name, 14);
                for (int r = 0; r < grades.Length; r++)
                {
                    int grade = grades[r];
                    float cy = 385 - r * 118;
                    if (c == 0) Caption(root, new Vector2(x0 - 70, cy), grade == 0 ? "sealed" : grade.ToString(), 16);
                    var cell = Ui.Placed($"{col.name} {grade}", root, new Vector2(cx, cy), new Vector2(widths[c], col.height));
                    cell.gameObject.AddComponent<RectMask2D>();
                    var copy = Copy(info.Code, Rarity.UltraRare, grade == 0 ? 10 : grade);
                    var slab = SlabView.Create(cell, new Vector2(0, -col.focus * col.scale), col.size, copy, info, set, sealedLabel: grade == 0);
                    slab.localScale = Vector3.one * col.scale;
                    Checked.Add((col.name, grade == 0 ? "sealed" : grade.ToString(), slab));
                }
            }
        }

        /// <summary>Logs the label fit check for the labels sheet: one line per label text.</summary>
        public static void ReportFit()
        {
            if (Checked.Count == 0) return;
            Canvas.ForceUpdateCanvases();
            int fails = 0;
            foreach (var (column, grade, slab) in Checked)
                foreach (var (child, size, width, room, ok) in SlabView.Fit(slab))
                {
                    if (!ok) fails++;
                    Debug.Log(System.FormattableString.Invariant($"slabfit: {column} {grade} {child} {(ok ? "ok" : "FAIL")} (size {size}, {width:0.0} of {room:0.0})"));
                }
            Debug.Log($"slabfit: {(fails == 0 ? "all fit" : $"{fails} FAIL")}");
        }

        /// <summary>The installed card with the longest name (ties: the lowest id).</summary>
        private static CardInfo? LongestName(ICardDataProvider cards)
        {
            CardInfo? best = null;
            foreach (var code in (CardView.Art?.Installed ?? new List<uint>()).OrderBy(c => c))
                if (cards.Get(code) is CardInfo info && (best == null || info.Name.Length > best.Name.Length)) best = info;
            return best;
        }

        /// <summary>Slabs and rims on the grounds they're seen on (see the class comment).</summary>
        private static void LayContext(RectTransform root, ICardDataProvider cards)
        {
            var stardust = cards.Get(44508094);
            var magician = cards.Get(46986414);
            if (stardust == null || magician == null) return;
            var gem = Copy(stardust.Code, Rarity.SecretRare, 10);
            var mint = Copy(magician.Code, Rarity.UltraRare, 9);
            var setGem = SetCode(stardust, Rarity.SecretRare);
            var setMint = SetCode(magician, Rarity.UltraRare);

            // Top row: Full at 0.62 on the panel, Full on the reveal scrim, Full at 0.62 on light parchment.
            var panel = Ground(root, new Vector2(-640, 200), new Vector2(300, 420), Ui.Hex("#1A1F2B"), "Full at 0.62 on the panel");
            SlabView.Create(panel, Vector2.zero, SlabView.Size.Full, mint, magician, setMint).localScale = Vector3.one * 0.62f;
            var scrim = Ground(root, new Vector2(-100, 140), new Vector2(420, 640), new Color(0, 0, 0, 0.78f), "Full on the reveal's scrim");
            SlabView.Create(scrim, Vector2.zero, SlabView.Size.Full, gem, stardust, setGem);
            var paper = Ground(root, new Vector2(420, 200), new Vector2(300, 420), Ui.Hex("#EFE6D2"), "Full at 0.62 on parchment");
            SlabView.Create(paper, Vector2.zero, SlabView.Size.Full, gem, stardust, setGem).localScale = Vector3.one * 0.62f;

            // Bottom row: grid rims at each screen's tile size, on its ground.
            var tiles = new (string name, Vector2 size, Color ground, CardCopy copy, CardInfo info, Vector2 at)[]
            {
                ("deck editor 88x128", new Vector2(88, 128), Ui.Hex("#161B27"), gem, stardust, new Vector2(-760, -350)),
                ("van shelf 120x174", new Vector2(120, 174), UiTheme.BackAlley.RowA, mint, magician, new Vector2(-560, -350)),
                ("van row 38x56", new Vector2(38, 56), UiTheme.BackAlley.RowA, gem, stardust, new Vector2(-380, -350)),
                ("desk row 40x58", new Vector2(40, 58), UiTheme.MainStreet.RowA, mint, magician, new Vector2(-240, -350)),
                ("shop 184x268", new Vector2(184, 268), Ui.Hex("#10131A"), gem, stardust, new Vector2(820, -270)),
            };
            foreach (var (name, size, ground, copy, info, at) in tiles)
            {
                var tile = Ground(root, at, size + new Vector2(40, 40), ground, name);
                SlabView.Rim(tile, Vector2.zero, size);
                var card = new FieldCard { Code = copy.Code, Type = info.Type, Level = info.Level, Attack = info.Attack, Defense = info.Defense, Position = 0x1, IsPublic = true };
                var view = CardView.Create(tile, Vector2.zero, size, card, info, 0, true, null, offField: true);
                foreach (var art in view.GetComponentsInChildren<RawImage>()) CardFoil.Apply(art, info, copy.Rarity, 0.6f, copy.Look);
                SlabView.RimGlass(tile, Vector2.zero, size, view);
            }
        }

        private static RectTransform Ground(RectTransform root, Vector2 at, Vector2 size, Color color, string caption)
        {
            var ground = Ui.Placed(caption, root, at, size);
            Ui.Image(ground, color).raycastTarget = false;
            Caption(root, at - new Vector2(0, size.y / 2 + 14), caption, 13);
            return ground;
        }

        private static void Caption(RectTransform root, Vector2 at, string text, int size)
        {
            var t = Ui.Text(Ui.Placed("Caption", root, at, new Vector2(1800, 22)), text, size, new Color(1, 1, 1, 0.75f));
            t.raycastTarget = false;
        }

        // ---- The reveal's frames ----------------------------------------------------------------------------

        /// <summary>
        /// Times (s) the reveal's frames are saved at: arriving, the sweep, the stamp (number growing in, ring flashing), the
        /// sticker's flash, and at rest; then two more at rest a second apart.
        /// </summary>
        private static readonly float[] RevealFrames = { 0.15f, 0.6f, 0.75f, 1.75f, 2.6f };

        /// <summary>
        /// `-slabreveal &lt;folder&gt;`: plays the desk's slab reveal (a Gem Mint Secret Rare) a frame at a time (1/60 s each,
        /// so it doesn't depend on the machine's speed) and saves slab-reveal-0150.png... at <see cref="RevealFrames"/>,
        /// then slab-reveal-rest-1.png and -rest-2.png a second apart at rest (which must be identical), then quits. With
        /// -reducedmotion it plays the calm version.
        /// </summary>
        public static void RunReveal(GameObject host, ICardDataProvider cards, string folder, Vector3? light)
        {
            CardFoil.Override = CardFoil.Display.On;
            CardFoil.FixedLight = light ?? CardFoil.IdleLight;
            host.AddComponent<SlabShowcase>().StartCoroutine(host.GetComponent<SlabShowcase>().Reveal(cards, folder));
        }

        private IEnumerator Reveal(ICardDataProvider cards, string folder)
        {
            Directory.CreateDirectory(folder);
            CardWear.Override = CardWear.Display.Full;
            CardWear.PrepareNow();
            var canvasGo = new GameObject("SlabReveal", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = Ui.Stretched("Root", canvasGo.transform, Vector2.zero, Vector2.one);
            Ui.Image(root, Ui.Hex("#10131A"));
            var overlay = Ui.Stretched("Reveal", root, Vector2.zero, Vector2.one);
            Ui.Image(overlay, new Color(0, 0, 0, 0.78f));
            var info = cards.Get(44508094); // Stardust Dragon
            if (info == null) { Application.Quit(); yield break; }
            var copy = Copy(info.Code, Rarity.SecretRare, 10);
            var set = SetCode(info, Rarity.SecretRare);
            yield return new WaitForSecondsRealtime(1); // pictures loaded
            var slab = SlabView.Create(overlay, new Vector2(0, 40), SlabView.Size.Full, copy, info, set, sealedLabel: true);
            slab.gameObject.AddComponent<CanvasGroup>().alpha = 0;
            const float step = 1f / 60;
            var play = SlabReveal.Play(slab, copy, info, set, () => false, null, () => step);
            int frame = 0, last = Mathf.RoundToInt(SlabReveal.RestAt * 60) + 120;
            for (; frame <= last; frame++)
            {
                play.MoveNext();
                foreach (var at in RevealFrames)
                    if (frame == Mathf.RoundToInt(at * 60)) Save(Path.Combine(folder, $"slab-reveal-{Mathf.RoundToInt(at * 1000):0000}.png"));
                if (frame == Mathf.RoundToInt(SlabReveal.RestAt * 60) + 60) Save(Path.Combine(folder, "slab-reveal-rest-1.png"));
                if (frame == last) Save(Path.Combine(folder, "slab-reveal-rest-2.png"));
                yield return null;
            }
            yield return new WaitForSecondsRealtime(1);
            Application.Quit();
        }

        private static void Save(string path)
        {
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"Saved slab reveal frame to {path}");
        }
    }
}
