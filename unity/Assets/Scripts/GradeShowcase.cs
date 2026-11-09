using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;
using Ygo.Engine.Data;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-gradeshowcase &lt;folder&gt;`: every grade from 10 to 1 side by side, screenshotted, then quits. Rows: a Common
    /// monster, a Spell (dark frame, light name), an Ultra Rare (foil under the wear), all at detail size, then a Secret
    /// Rare at the board's size and foil strength (where scratches give way and creases stay a pixel wide). grades.png
    /// has wear on; grades-label-only.png the "Card grades: label only" setting (no wear, labels only). The light is
    /// fixed (`-foillight x,y,hover`, default <see cref="CardFoil.IdleLight"/>), so two runs give the same pictures.
    /// `-gradesweep N` instead takes N shots with wear on (grades-sweep-1.png...) as a hovered light crosses each card from
    /// top left to bottom right, where scratches, dents and creases catch it.
    /// </summary>
    public sealed class GradeShowcase : MonoBehaviour
    {
        public static void Run(GameObject host, ICardDataProvider cards, string folder, Vector3? light, int sweep = 0)
        {
            CardFoil.Override = CardFoil.Display.On;
            CardFoil.FixedLight = light;
            var showcase = host.AddComponent<GradeShowcase>();
            showcase.StartCoroutine(sweep > 0 ? showcase.Sweep(cards, folder, sweep) : showcase.Show(cards, folder));
        }

        private IEnumerator Sweep(ICardDataProvider cards, string folder, int shots)
        {
            Directory.CreateDirectory(folder);
            CardWear.PrepareNow();
            CardWear.Override = CardWear.Display.Full;
            Build(cards);
            yield return new WaitForSecondsRealtime(1.2f);
            for (int shot = 1; shot <= shots; shot++)
            {
                float t = shots == 1 ? 0.5f : (shot - 1f) / (shots - 1);
                CardFoil.FixedLight = new Vector3(Mathf.Lerp(0.05f, 0.95f, t), Mathf.Lerp(0.95f, 0.05f, t), 1);
                // The cards take the new light in their next Update; save the frame after that.
                yield return null;
                yield return null;
                var path = Path.Combine(folder, $"grades-sweep-{shot}.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"Saved grade sweep to {path}");
                yield return new WaitForSecondsRealtime(0.25f);
            }
            yield return new WaitForSecondsRealtime(0.6f);
            Application.Quit();
        }

        private IEnumerator Show(ICardDataProvider cards, string folder)
        {
            Directory.CreateDirectory(folder);
            CardWear.PrepareNow();
            foreach (var mode in new[] { CardWear.Display.Full, CardWear.Display.LabelOnly })
            {
                CardWear.Override = mode;
                var canvas = Build(cards);
                yield return new WaitForSecondsRealtime(1.2f);
                var path = Path.Combine(folder, mode == CardWear.Display.Full ? "grades.png" : "grades-label-only.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"Saved grade showcase to {path}");
                yield return new WaitForSecondsRealtime(0.6f);
                Destroy(canvas);
            }
            Application.Quit();
        }

        private static GameObject Build(ICardDataProvider cards)
        {
            var canvasGo = new GameObject("GradeShowcase", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var root = Ui.Stretched("Root", canvasGo.transform, Vector2.zero, Vector2.one);
            Ui.Image(root, Ui.Hex("#10131A"));

            var detail = new Vector2(150, 219);
            var rows = new (uint code, Rarity rarity, Vector2 size, float strength, float y)[]
            {
                (89631139, Rarity.Common, detail, 1f, 395),         // Blue-Eyes White Dragon
                (55144522, Rarity.Common, detail, 1f, 130),         // Pot of Greed
                (46986414, Rarity.UltraRare, detail, 1f, -135),     // Dark Magician
                (44508094, Rarity.SecretRare, CardView.FieldSize, FoilShowcase.BoardStrength, -355), // Stardust Dragon, as on the board
            };
            foreach (var (code, rarity, size, strength, y) in rows)
            {
                var info = cards.Get(code);
                if (info == null) continue;
                for (int grade = 10; grade >= 1; grade--)
                {
                    var center = new Vector2((10 - grade - 4.5f) * 186, y);
                    var card = Ui.Placed($"{info.Name} {grade}", root, center, size);
                    var raw = card.gameObject.AddComponent<RawImage>();
                    raw.texture = CardView.Art?.Get(code);
                    var look = new CopyLook(rarity, grade, Hash.Mix(code, (uint)grade));
                    CardFoil.Apply(raw, info, rarity, strength, look);
                    if (size.y < 100 || y < 0) continue;
                    var label = Ui.Text(Ui.Placed("Label", root, center + new Vector2(0, -size.y / 2 - 14), new Vector2(180, 22)),
                        $"<color={Grades.Color(grade)}>{Grades.Glyph(grade)} {Grades.Label(grade)}</color>", 15, Color.white);
                    label.supportRichText = true;
                }
            }
            var mode = Ui.Text(Ui.Placed("Mode", root, new Vector2(0, -470), new Vector2(1200, 30)),
                $"{CardWear.ModeLabel}  ·  rows: Common, Common Spell, Ultra Rare, Secret Rare at board size", 16, new Color(1, 1, 1, 0.6f));
            mode.raycastTarget = false;
            return canvasGo;
        }
    }
}
