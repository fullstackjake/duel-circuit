using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A poster as it hangs on your plate or in the Locker: a paper bill, tilted and taped, with a title band over its
    /// card's artwork (cropped from the card picture). Without a picture it's the title on the poster's ink. The lower
    /// third stays quiet (a tagline only, left off on the plate, where the name and LP sit).
    /// </summary>
    public static class PosterView
    {
        public static readonly Vector2 Size = new Vector2(240, 320);
        /// <summary>The artwork's square in a card picture (EDOPro's 177x254: 136x136 at 21,46 from the top left), as a UV rect.</summary>
        private static readonly Rect ArtBox = new Rect(21f / 177, 1 - 182f / 254, 136f / 177, 136f / 254);

        /// <summary>Builds the poster at full size, centered at <paramref name="center"/>; scale the result to shrink it.</summary>
        public static RectTransform Create(Transform parent, Poster poster, Vector2 center, float tilt = -3f, bool tagline = true)
        {
            var root = Ui.Placed("Poster", parent, center, Size);
            root.localRotation = Quaternion.Euler(0, 0, tilt);
            var ink = Ui.Hex(poster.Ink);

            Image(root, "Shadow", new Vector2(5, -7), Size + new Vector2(8, 8), new Color(0, 0, 0, 0.45f));
            // A faint edge, a little darker than the sheet.
            Image(root, "Edge", Vector2.zero, Size + new Vector2(2, 2), new Color(0.55f, 0.48f, 0.36f, 0.9f));
            Image(root, "Paper", Vector2.zero, Size, Ui.Hex("#EFE6D2"));

            // The title band.
            Image(root, "Band", new Vector2(0, 128), new Vector2(222, 46), ink);
            var title = Ui.Text(Ui.Placed("Title", root, new Vector2(0, 128), new Vector2(210, 42)), poster.Title.ToUpperInvariant(), 26, Ui.Hex("#FBF6EA"));
            title.fontStyle = FontStyle.Bold;
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = 14;
            title.resizeTextMaxSize = 26;
            title.raycastTarget = false;

            // The artwork, or the title's first letter on the ink when the card has no picture.
            var window = Ui.Placed("Art", root, new Vector2(0, -12), new Vector2(222, 222));
            var art = CardView.Art?.Get(poster.Card);
            if (art != null)
            {
                var raw = window.gameObject.AddComponent<RawImage>();
                raw.texture = art;
                raw.uvRect = ArtBox;
                raw.raycastTarget = false;
            }
            else
            {
                Ui.Image(window, Color.Lerp(ink, Color.black, 0.35f)).raycastTarget = false;
                var letter = Ui.Text(window, poster.Title.Substring(0, 1), 120, new Color(1, 1, 1, 0.85f));
                letter.fontStyle = FontStyle.Bold;
                letter.raycastTarget = false;
            }

            if (tagline)
            {
                var line = Ui.Text(Ui.Placed("Tagline", root, new Vector2(0, -141), new Vector2(222, 28)), poster.Tagline, 12, Ui.Hex("#4A4034"));
                line.fontStyle = FontStyle.Italic;
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = 8;
                line.resizeTextMaxSize = 12;
                line.raycastTarget = false;
            }

            // Tape over the top corners.
            foreach (var side in new[] { -1, 1 })
            {
                var tape = Image(root, "Tape", new Vector2(side * 104, 154), new Vector2(64, 20), new Color(0.93f, 0.89f, 0.74f, 0.75f)).rectTransform;
                tape.localRotation = Quaternion.Euler(0, 0, side * -32);
            }
            return root;
        }

        private static Image Image(RectTransform parent, string name, Vector2 center, Vector2 size, Color color)
        {
            var image = Ui.Image(Ui.Placed(name, parent, center, size), color);
            image.raycastTarget = false;
            return image;
        }
    }
}
