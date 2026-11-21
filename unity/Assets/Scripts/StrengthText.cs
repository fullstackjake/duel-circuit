using Ygo.Engine.Campaign;

namespace YgoCampaign.Game
{
    /// <summary>
    /// A deck's strength as rich text (THE-CLIMB-SPEC section 7): five ★, the first <c>stars</c> in the theme's gold and the
    /// rest dim, at the size of the text they sit in (the text needs <c>supportRichText</c>). Circuit and Tag Cup rows, the
    /// guests' decks line and the Recipe Book share it.
    /// </summary>
    public static class StrengthText
    {
        public const char Star = '★';

        /// <summary>"" for an unrated deck (0), else five ★: <paramref name="stars"/> in <see cref="UiTheme.GoldHex"/>, the rest in <see cref="UiTheme.TextDimHex"/>.</summary>
        public static string Stars(int stars, UiTheme T)
        {
            if (stars <= 0) return "";
            if (stars > DeckStrength.MaxStars) stars = DeckStrength.MaxStars;
            var gold = $"<color={T.GoldHex}>{new string(Star, stars)}</color>";
            return stars == DeckStrength.MaxStars ? gold : gold + $"<color={T.TextDimHex}>{new string(Star, DeckStrength.MaxStars - stars)}</color>";
        }
    }
}
