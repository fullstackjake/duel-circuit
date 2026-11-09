using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ygo.Engine.Data
{
    /// <summary>
    /// Compact binary card database. The harness exports it from BabelCDB's SQLite files so the
    /// game itself never needs a SQLite library (which is awkward in Unity across platforms).
    /// </summary>
    public sealed class CardPack : ICardDataProvider
    {
        private const uint Magic = 0x4B504359; // "YCPK"
        private const int Version = 3; // 3 added Category; 2 still reads (no categories)

        private readonly Dictionary<uint, CardInfo> _cards = new Dictionary<uint, CardInfo>();

        public IEnumerable<CardInfo> All => _cards.Values;
        public int Count => _cards.Count;

        public CardInfo? Get(uint code) => _cards.TryGetValue(code, out var info) ? info : null;

        public static void Write(IEnumerable<CardInfo> cards, Stream stream)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            var list = new List<CardInfo>(cards);
            w.Write(Magic);
            w.Write(Version);
            w.Write(list.Count);
            foreach (var c in list)
            {
                w.Write(c.Code);
                w.Write(c.Alias);
                w.Write((byte)c.Setcodes.Length);
                foreach (var sc in c.Setcodes) w.Write(sc);
                w.Write(c.Type);
                w.Write(c.Level);
                w.Write(c.Attribute);
                w.Write(c.Race);
                w.Write(c.Attack);
                w.Write(c.Defense);
                w.Write(c.LScale);
                w.Write(c.RScale);
                w.Write(c.LinkMarker);
                w.Write(c.Category);
                w.Write(c.Name);
                w.Write(c.Text);
                // Only up to the last non-empty string, to keep the pack small.
                int count = c.Strings.Length;
                while (count > 0 && string.IsNullOrEmpty(c.Strings[count - 1])) count--;
                w.Write((byte)count);
                for (int i = 0; i < count; i++) w.Write(c.Strings[i] ?? "");
            }
        }

        public static CardPack Read(Stream stream)
        {
            var pack = new CardPack();
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a card pack");
            int version = r.ReadInt32();
            if (version != Version && version != 2) throw new InvalidDataException("Unsupported card pack version");
            var count = r.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var c = new CardInfo { Code = r.ReadUInt32(), Alias = r.ReadUInt32() };
                c.Setcodes = new ushort[r.ReadByte()];
                for (int s = 0; s < c.Setcodes.Length; s++) c.Setcodes[s] = r.ReadUInt16();
                c.Type = r.ReadUInt32();
                c.Level = r.ReadUInt32();
                c.Attribute = r.ReadUInt32();
                c.Race = r.ReadUInt64();
                c.Attack = r.ReadInt32();
                c.Defense = r.ReadInt32();
                c.LScale = r.ReadUInt32();
                c.RScale = r.ReadUInt32();
                c.LinkMarker = r.ReadUInt32();
                if (version >= 3) c.Category = r.ReadUInt64();
                c.Name = r.ReadString();
                c.Text = r.ReadString();
                c.Strings = new string[r.ReadByte()];
                for (int s = 0; s < c.Strings.Length; s++) c.Strings[s] = r.ReadString();
                pack._cards[c.Code] = c;
            }
            return pack;
        }

        public static CardPack Load(string path)
        {
            using var file = File.OpenRead(path);
            return Read(file);
        }
    }
}
