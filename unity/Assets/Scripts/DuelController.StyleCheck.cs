using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-stylecheck &lt;folder&gt;` (with -mods &lt;empty folder&gt;): card art styles. The first run makes plain test pictures in
    /// mods/cards/styles/extended and /manga (the manga one wide, so it must be letterboxed rather than stretched), picks
    /// Blue-Eyes's style with the deck editor's Artwork button and the card reader's, sets the preferred style, and checks
    /// what's drawn and what's saved. Run it again with the same folders: the second run checks the choices came back
    /// after the restart. Screenshots of the editor in each style, the reader and the Settings row.
    /// </summary>
    public sealed partial class DuelController
    {
        private const uint StyleCard = 89631139, StylePreferredCard = 46986414, StyleNone = 55144522; // Blue-Eyes, Dark Magician, Pot of Greed

        private System.Collections.IEnumerator StyleCheck(string folder)
        {
            Directory.CreateDirectory(folder);
            System.Collections.IEnumerator Shot(string name)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"style-{name}.png"));
                yield return null;
                yield return null;
            }
            void Report(string what, bool ok) => Debug.Log($"style check: {(ok ? "ok  " : "FAIL")} {what}");
            yield return new WaitForSecondsRealtime(1f);
            var art = CardView.Art!;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mods") < 0)
            {
                Report("needs -mods <empty folder> (it writes test pictures and choices there)", false);
                Application.Quit();
                yield break;
            }
            string Name(uint code) => art.Get(code) is Texture2D t ? ArtLoader.PlainName(t) : "(none)";
            var marker = Path.Combine(folder, "style-picked.txt");
            var blueEyes = _cardPack!.Get(StyleCard)!;

            if (File.Exists(marker))
            {
                // After the restart: the choices were read back from mods/cards/art-styles.txt.
                Report($"after a restart: the preferred style is still {CardArt.Label(art.Preferred)}", art.Preferred == "extended");
                Report($"after a restart: Blue-Eyes is still drawn in its chosen style ({Name(StyleCard)})",
                    art.ChoiceFor(StyleCard) == "manga" && Name(StyleCard) == $"art {StyleCard} manga");
                Report($"after a restart: Dark Magician keeps Standard over the preference ({Name(StylePreferredCard)})",
                    art.StyleOf(StylePreferredCard) == CardArt.Standard && !Name(StylePreferredCard).Contains("extended"));
                _editor.Open("classic");
                yield return new WaitForSecondsRealtime(0.6f);
                var drawn = _editor.DebugDrawnArt();
                Report($"after a restart: the deck editor draws the deck's Blue-Eyes in manga style ({drawn.Count(n => n == $"art {StyleCard} manga")} of 3)",
                    drawn.Count(n => n == $"art {StyleCard} manga") >= 3);
                _editor.DebugDetail(blueEyes);
                yield return null;
                yield return Shot("restart-editor");
                File.Delete(marker);
                Application.Quit();
                yield break;
            }

            // Plain test pictures (bands of color with a big letter-free stripe pattern), not card art.
            void Picture(string style, uint code, int w, int h, Color a, Color b)
            {
                var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        pixels[y * w + x] = ((x + y) / 24) % 2 == 0 ? a : b;
                texture.SetPixels32(pixels);
                var dir = Path.Combine(art.StylesRoot, style);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, $"{code}.png"), texture.EncodeToPNG());
                Destroy(texture);
            }
            Picture("extended", StyleCard, 343, 500, new Color(0.2f, 0.45f, 0.9f), new Color(0.85f, 0.9f, 1f));
            Picture("extended", StylePreferredCard, 343, 500, new Color(0.5f, 0.2f, 0.7f), new Color(0.95f, 0.85f, 1f));
            Picture("manga", StyleCard, 500, 320, new Color(0.1f, 0.1f, 0.1f), new Color(0.92f, 0.92f, 0.92f));
            art.ScanStyles();
            Report($"style folders found: {string.Join(", ", art.Styles)}", art.Styles.SequenceEqual(new[] { "extended", "manga" }));
            Report($"Blue-Eyes has {string.Join(", ", art.StylesFor(StyleCard).Select(CardArt.Label))}",
                art.StylesFor(StyleCard).SequenceEqual(new[] { CardArt.Standard, "extended", "manga" }));
            Report("Pot of Greed (no style pictures) has only Standard", art.StylesFor(StyleNone).Count == 1);

            // The deck editor: the Artwork button on Blue-Eyes, through each style.
            _editor.Open("classic");
            yield return new WaitForSecondsRealtime(0.6f);
            var detail = _editor.DebugDetail(blueEyes);
            yield return null;
            Report($"the deck editor shows an Artwork button on Blue-Eyes (\"{detail.StyleButton}\")", detail.StyleButton?.Contains("Standard") == true);
            yield return Shot("editor-standard");
            detail.DebugClickStyle();
            yield return null;
            var drawn1 = _editor.DebugDrawnArt();
            Report($"Artwork: Blue-Eyes switches to extended art (\"{detail.StyleButton}\", {Name(StyleCard)})",
                detail.StyleButton?.Contains("Extended") == true && Name(StyleCard) == $"art {StyleCard} extended");
            Report($"the deck's copies redraw in it ({drawn1.Count(n => n == $"art {StyleCard} extended")} of 3)", drawn1.Count(n => n == $"art {StyleCard} extended") >= 3);
            yield return Shot("editor-extended");
            detail.DebugClickStyle();
            yield return null;
            var manga = art.Get(StyleCard);
            float aspect = manga == null ? 0 : (float)manga.width / manga.height;
            Report($"Artwork: then manga (\"{detail.StyleButton}\"); its 500x320 picture is letterboxed to {manga?.width}x{manga?.height}, not stretched",
                detail.StyleButton?.Contains("Manga") == true && Mathf.Abs(aspect - CardArt.CardAspect) < 0.01f);
            yield return Shot("editor-manga");
            _editor.DebugDetail(_cardPack.Get(StyleNone)!);
            Report("no Artwork button on a card with only Standard pictures", detail.StyleButton == null);

            // The preferred style: cards with no choice of their own use it; a card's own choice (even Standard) wins.
            art.Preferred = "extended";
            Report($"preferring extended: Dark Magician uses it ({Name(StylePreferredCard)}), Blue-Eyes keeps manga ({Name(StyleCard)})",
                Name(StylePreferredCard) == $"art {StylePreferredCard} extended" && Name(StyleCard) == $"art {StyleCard} manga");
            art.Choose(StylePreferredCard, CardArt.Standard);
            Report($"Dark Magician set back to Standard stays Standard over the preference ({Name(StylePreferredCard)})",
                art.ChoiceFor(StylePreferredCard) == CardArt.Standard && !Name(StylePreferredCard).Contains("extended"));
            var older = _cardPack.All.FirstOrDefault(c => c.Alias == StyleCard && art.ArtKey(c.Code) == StyleCard);
            if (older != null)
                Report($"{older.Name} ({older.Code}, no picture of its own) follows Blue-Eyes's style", art.StyleOf(older.Code) == "manga");

            // The duel's card reader: its Artwork button too.
            _editor.Close();
            _view.OpenReader(blueEyes);
            yield return null;
            var reader = _view.ReaderDetail;
            Report($"the card reader shows the Artwork button (\"{reader?.StyleButton}\")", reader?.StyleButton?.Contains("Manga") == true);
            yield return Shot("reader-manga");
            reader?.DebugClickStyle();
            yield return null;
            Report($"Artwork in the reader: Blue-Eyes to Standard ({Name(StyleCard)})", art.StyleOf(StyleCard) == CardArt.Standard && art.ChoiceFor(StyleCard) == CardArt.Standard);
            reader?.DebugClickStyle();
            Report("then extended, the preferred style (its own choice cleared)", art.StyleOf(StyleCard) == "extended" && art.ChoiceFor(StyleCard) == null);
            reader?.DebugClickStyle();
            yield return null;
            Report($"then manga again ({Name(StyleCard)})", art.ChoiceFor(StyleCard) == "manga");
            yield return Shot("reader-back-to-manga");
            _view.CloseReader();

            // Settings: the preferred style row.
            _settings.Open();
            yield return null;
            var row = _settings.DebugShowRow("Prefer style");
            Report($"Settings has \"Prefer style: {row}\"", row == "Extended");
            yield return new WaitForSecondsRealtime(0.2f);
            yield return Shot("settings");
            art.CyclePreferred();
            var next = art.Preferred;
            art.CyclePreferred();
            var after = art.Preferred;
            art.CyclePreferred();
            Report($"Prefer style cycles extended > {CardArt.Label(next)} > {CardArt.Label(after)} > {CardArt.Label(art.Preferred)}",
                next == "manga" && after == CardArt.Standard && art.Preferred == "extended");
            _settings.Close();

            // Saved: a fresh lookup from the same mods folder reads the same choices.
            var saved = File.Exists(art.ChoicesFile) ? File.ReadAllText(art.ChoicesFile) : "";
            var reread = new CardArt(_cardPack, art.ModsRoot, art.EdoproFolder, art.HdFolders.Count > 0 ? art.HdFolders : new[] { "off" });
            Report($"saved in {Path.GetFileName(art.ChoicesFile)}: {saved.Replace('\n', ' ').Trim()}",
                reread.Preferred == "extended" && reread.ChoiceFor(StyleCard) == "manga" && reread.ChoiceFor(StylePreferredCard) == CardArt.Standard);
            File.WriteAllText(marker, "Run -stylecheck again with the same -mods and folder: it checks the choices after the restart.\n");
            Report("first run done: run it again to check the restart", true);
            Application.Quit();
        }
    }
}
