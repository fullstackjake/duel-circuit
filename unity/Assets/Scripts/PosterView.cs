using UnityEngine;
using UnityEngine.UI;
using Ygo.Engine;
using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A poster as it hangs on your plate or in the Locker: a paper bill, tilted and taped, with a title band over its
    /// card's artwork (cropped from the card picture; without one, the plain card's window: its pattern and glyph). The
    /// lower third stays quiet (a tagline only, left off on the plate, where the name and LP sit).
    /// </summary>
    public static class PosterView
    {
        public static readonly Vector2 Size = new Vector2(240, 320);

        /// <summary>Card data for the plain cards' windows (set once at start; posters are built without it).</summary>
        public static ICardDataProvider? Cards { get; set; }

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
            var title = Ui.Text(Ui.Placed("Title", root, new Vector2(0, 128), new Vector2(210, 42)), poster.Title.ToUpperInvariant(), TextRole.Heading, Ui.Hex("#FBF6EA"));
            Typography.Strong(title);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = UiTheme.Floor;
            title.resizeTextMaxSize = UiTheme.Size(TextRole.Heading);
            title.raycastTarget = false;

            // The artwork: the picture's art square, or the middle of a plain card's window (its glyph), square.
            var window = Ui.Placed("Art", root, new Vector2(0, -12), new Vector2(222, 222));
            var raw = window.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var art = CardView.Art?.Get(poster.Card);
            // A Standard face (its window has the glyph in the middle) wide enough that the square crop is sharp.
            var texture = art ?? CardFace.PlainOnly(poster.Card, Cards?.Get(poster.Card), CardLayout.Standard,
                CardFace.PixelWidth(CardLayout.Standard, 222 / (0.29f / CardFace.Aspect)));
            raw.texture = texture;
            CardFace.Tag(raw, poster.Card, new Vector2(200, 292), CardFit.Normal);
            var uv = CardFace.WindowUv(texture);
            if (CardFace.IsPlain(texture))
            {
                float side = uv.height * texture.height / texture.width; // the window's height, as a width in UV
                uv = new Rect(uv.center.x - side / 2, uv.y, side, uv.height);
            }
            raw.uvRect = uv;

            if (tagline)
            {
                var line = Ui.Text(Ui.Placed("Tagline", root, new Vector2(0, -142), new Vector2(222, 36)), poster.Tagline, TextRole.Caption, Ui.Hex("#4A4034"));
                Typography.Italic(line);
                line.resizeTextForBestFit = true;
                line.resizeTextMinSize = UiTheme.Floor;
                line.resizeTextMaxSize = UiTheme.Size(TextRole.Caption);
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
