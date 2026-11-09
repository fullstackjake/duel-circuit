using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ygo.Engine.DeckBuilding
{
    /// <summary>
    /// Deck sharing formats used by EDOPro and the wider community:
    /// .ydk text files, and YDKE links ("ydke://main!extra!side!", each part base64 of little-endian uint32 ids).
    /// </summary>
    public static class DeckCodes
    {
        public static string ToYdk(Deck deck)
        {
            var sb = new StringBuilder();
            sb.Append("#created by Duel Circuit\n");
            if (deck.Campaign) sb.Append("#campaign\n");
            foreach (var key in Deck.NoteKeys)
                if (deck.Notes.TryGetValue(key, out var value) && value.Length > 0) sb.Append('#').Append(key).Append(' ').Append(value).Append('\n');
            if (deck.Starters.Count > 0) sb.Append("#starters ").Append(string.Join(" ", deck.Starters)).Append('\n');
            sb.Append("#main\n");
            foreach (var c in deck.Main) sb.Append(c).Append('\n');
            sb.Append("#extra\n");
            foreach (var c in deck.Extra) sb.Append(c).Append('\n');
            sb.Append("!side\n");
            foreach (var c in deck.Side) sb.Append(c).Append('\n');
            return sb.ToString();
        }

        public static string ToYdke(Deck deck) =>
            $"ydke://{Pack(deck.Main)}!{Pack(deck.Extra)}!{Pack(deck.Side)}!";

        /// <summary>Reads a YDKE link or .ydk text; returns null if it is neither.</summary>
        public static Deck? Parse(string text, string name = "")
        {
            text = text.Trim();
            if (text.StartsWith("ydke://", StringComparison.OrdinalIgnoreCase))
            {
                var parts = text.Substring(7).Split('!');
                if (parts.Length < 3) return null;
                try
                {
                    return new Deck { Name = name, Main = Unpack(parts[0]), Extra = Unpack(parts[1]), Side = Unpack(parts[2]) };
                }
                catch (FormatException)
                {
                    return null;
                }
            }
            if (text.Contains("#main")) return Deck.ParseYdk(text, name);
            return null;
        }

        private static string Pack(List<uint> codes)
        {
            var bytes = new byte[codes.Count * 4];
            for (int i = 0; i < codes.Count; i++) BitConverter.GetBytes(codes[i]).CopyTo(bytes, i * 4);
            return Convert.ToBase64String(bytes);
        }

        private static List<uint> Unpack(string base64)
        {
            if (base64.Length == 0) return new List<uint>();
            var bytes = Convert.FromBase64String(base64);
            return Enumerable.Range(0, bytes.Length / 4).Select(i => BitConverter.ToUInt32(bytes, i * 4)).ToList();
        }
    }
}
