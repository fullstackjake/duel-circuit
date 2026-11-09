using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Ygo.Engine.AI;
using Ygo.Engine.Data;
using Ygo.Engine.Protocol;

namespace Ygo.Engine.Replays
{
    /// <summary>
    /// A recorded duel: the settings and decks it started from plus every answer either player gave. The core is
    /// deterministic, so feeding the same answers back reproduces the duel exactly, move for move.
    /// Saved and shared as a short text code ("YGOR1:..."), which is the same thing as the file contents.
    /// </summary>
    public sealed class Replay
    {
        private const string Prefix = "YGOR1:";
        private const uint Magic = 0x4C505259; // "YRPL"
        /// <summary>
        /// 2 added tag duels (partners), 3 the looks of your copies, 4 their slabs, 5 each side's mat, 6 the duel shuffles a
        /// Deck an effect looked through (<see cref="DuelFlags.ShuffleAfterDeckLook"/>: same layout as 5, but a game before it
        /// would play the duel out differently, so it refuses one); older replays still load. A replay with neither a mat nor
        /// that flag is still written as 4 (<see cref="Write"/>), so the game before 5 can read it.
        /// </summary>
        private const int Version = 6;

        public DuelSettings Settings = new DuelSettings();
        public Deck[] Decks = { new Deck(), new Deck() };
        /// <summary>Display names, character ids ("you" for a human), and deck names, per seat.</summary>
        public string[] Names = { "", "" };
        public string[] Characters = { "", "" };
        public string[] DeckNames = { "", "" };
        /// <summary>Seat a human played, or -1 for bot vs bot.</summary>
        public int HumanSeat = -1;
        /// <summary>0 or 1, 2 for a draw, -1 if the replay stops before the end.</summary>
        public int Winner = -1;
        public int Turns;
        public DateTime Date = DateTime.Now;
        /// <summary>Card count of the database it was recorded with; a different count may mean changed cards.</summary>
        public int CardCount;
        public List<byte[]> Responses = new List<byte[]>();

        /// <summary>Tag duels: each team's partner (name, character, deck), by team; null for one-on-one duels.</summary>
        public string[]? PartnerNames, PartnerCharacters, PartnerDeckNames;
        public Deck[]? Partners;

        public bool IsTag => Partners != null;

        /// <summary>
        /// The copies a seat played (rarity, grade, wear seed per main- then extra-deck slot), so the replay shows them
        /// as they were even after the collection changes; null for a seat with no collection behind it (a CPU).
        /// </summary>
        public CopyLook[]?[] Looks = { null, null };

        /// <summary>
        /// Each side's mat when the duel chose it by name (campaign/mats.txt): the Library era mat you put on, an alley table's
        /// mat; "" for one the replay rebuilds from its seat (a character's signature mat, a picture, the deck's look). Version 5;
        /// "" in older replays.
        /// </summary>
        public string[] Mats = { "", "" };

        public string Title
        {
            get
            {
                string Team(int t) => IsTag ? $"{Names[t]} & {PartnerNames![t]}" : $"{Names[t]} ({DeckNames[t]})";
                var result = Winner == 2 ? "draw" : Winner >= 0 ? (IsTag ? $"{Names[Winner]} & {PartnerNames![Winner]} won" : $"{Names[Winner]} won") : "unfinished";
                return $"{Team(0)} vs {Team(1)} · {result}, turn {Turns}";
            }
        }

        /// <summary>A recipe that recreates the starting position.</summary>
        public DuelRecipe Recipe(ICardDataProvider cards, IScriptProvider scripts)
        {
            var settings = new DuelSettings
            {
                Flags = Settings.Flags, StartingLP = Settings.StartingLP, StartingHand = Settings.StartingHand,
                DrawPerTurn = Settings.DrawPerTurn, Seed = Settings.Seed,
            };
            // Each seat's deck shows the copies recorded with it.
            Deck Seat(int seat)
            {
                var deck = Decks[seat].Clone();
                deck.Looks = Looks[seat];
                return deck;
            }
            if (IsTag)
                return TagDuel.Recipe(cards, scripts, settings, Seat(0), Partners![0].Clone(), Seat(1), Partners[1].Clone());
            return new DuelRecipe { Cards = cards, Scripts = scripts, Settings = settings, Deck0 = Seat(0), Deck1 = Seat(1) };
        }

        // ---- Text code -------------------------------------------------------------------------------

        /// <summary>
        /// Where a code opens (a position in redraws, as the game counts them while watching); 0 for the start. Not part of
        /// the replay itself: it rides on the code as "@position".
        /// </summary>
        public int OpenAt;

        /// <summary>The replay as a text code; with <paramref name="at"/> &gt; 0 it opens at that position.</summary>
        public string ToCode(int at)
        {
            var code = ToCode();
            return at > 0 ? $"{code}@{at}" : code;
        }

        public string ToCode()
        {
            using var raw = new MemoryStream();
            using (var deflate = new DeflateStream(raw, CompressionLevel.Optimal, leaveOpen: true))
                Write(deflate);
            return Prefix + Convert.ToBase64String(raw.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Reads a replay code (whitespace and line breaks are ignored); null if it isn't one.</summary>
        public static Replay? FromCode(string text)
        {
            var code = new StringBuilder();
            foreach (var c in text.Trim())
                if (!char.IsWhiteSpace(c)) code.Append(c);
            var s = code.ToString();
            var start = s.IndexOf(Prefix, StringComparison.Ordinal);
            if (start < 0) return null;
            s = s.Substring(start + Prefix.Length);
            // "@1234" at the end: where it opens.
            int openAt = 0;
            var at = s.LastIndexOf('@');
            if (at >= 0)
            {
                if (!int.TryParse(s.Substring(at + 1), out openAt) || openAt < 0) openAt = 0;
                s = s.Substring(0, at);
            }
            s = s.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            try
            {
                using var raw = new MemoryStream(Convert.FromBase64String(s));
                using var deflate = new DeflateStream(raw, CompressionMode.Decompress);
                using var buffer = new MemoryStream();
                deflate.CopyTo(buffer);
                buffer.Position = 0;
                var replay = Read(buffer);
                if (replay != null) replay.OpenAt = openAt;
                return replay;
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidDataException || ex is EndOfStreamException)
            {
                return null;
            }
        }

        public void Save(string path) => File.WriteAllText(path, ToCode() + "\n");

        public static Replay? Load(string path) => FromCode(File.ReadAllText(path));

        // ---- Binary layout ---------------------------------------------------------------------------

        private void Write(Stream stream)
        {
            using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
            w.Write(Magic);
            // Format 6 when the duel shuffles after a Deck look (a game before 6 would replay it wrong), 5 when it has a mat to
            // say, otherwise 4, which the game before format 5 still reads. 5 and 6 share a layout.
            bool shuffles = (Settings.Flags & DuelFlags.ShuffleAfterDeckLook) != 0;
            bool mats = shuffles || Array.Exists(Mats, m => !string.IsNullOrEmpty(m));
            w.Write(shuffles ? Version : mats ? 5 : 4);
            w.Write(Settings.Flags);
            w.Write(Settings.StartingLP);
            w.Write(Settings.StartingHand);
            w.Write(Settings.DrawPerTurn);
            w.Write(Settings.Seed);
            for (int seat = 0; seat < 2; seat++)
            {
                w.Write(Names[seat]);
                w.Write(Characters[seat]);
                w.Write(DeckNames[seat]);
                WriteCards(w, Decks[seat].Main);
                WriteCards(w, Decks[seat].Extra);
            }
            w.Write(IsTag);
            if (IsTag)
                for (int seat = 0; seat < 2; seat++)
                {
                    w.Write(PartnerNames![seat]);
                    w.Write(PartnerCharacters![seat]);
                    w.Write(PartnerDeckNames![seat]);
                    WriteCards(w, Partners![seat].Main);
                    WriteCards(w, Partners[seat].Extra);
                }
            w.Write((sbyte)HumanSeat);
            w.Write((sbyte)Winner);
            w.Write(Turns);
            w.Write(Date.ToUniversalTime().Ticks);
            w.Write(CardCount);
            w.Write(Responses.Count);
            foreach (var response in Responses)
            {
                WriteVarint(w, (uint)response.Length);
                w.Write(response);
            }
            for (int seat = 0; seat < 2; seat++)
            {
                var looks = Looks[seat];
                w.Write(looks != null);
                if (looks == null) continue;
                w.Write((ushort)looks.Length);
                foreach (var look in looks)
                {
                    w.Write((byte)look.Rarity);
                    w.Write((byte)look.Grade);
                    w.Write(look.Seed);
                    w.Write((byte)look.Slab);
                }
            }
            if (mats)
                for (int seat = 0; seat < 2; seat++) w.Write(Mats[seat] ?? "");
        }

        private static Replay Read(Stream stream)
        {
            using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a replay");
            var version = r.ReadInt32();
            if (version < 1 || version > Version) throw new InvalidDataException("Unsupported replay version");
            var replay = new Replay();
            replay.Settings = new DuelSettings
            {
                Flags = r.ReadUInt64(), StartingLP = r.ReadUInt32(), StartingHand = r.ReadUInt32(),
                DrawPerTurn = r.ReadUInt32(), Seed = r.ReadUInt64(),
            };
            for (int seat = 0; seat < 2; seat++)
            {
                replay.Names[seat] = r.ReadString();
                replay.Characters[seat] = r.ReadString();
                replay.DeckNames[seat] = r.ReadString();
                replay.Decks[seat] = new Deck { Name = replay.DeckNames[seat], Main = ReadCards(r), Extra = ReadCards(r) };
            }
            if (version >= 2 && r.ReadBoolean())
            {
                replay.PartnerNames = new string[2];
                replay.PartnerCharacters = new string[2];
                replay.PartnerDeckNames = new string[2];
                replay.Partners = new Deck[2];
                for (int seat = 0; seat < 2; seat++)
                {
                    replay.PartnerNames[seat] = r.ReadString();
                    replay.PartnerCharacters[seat] = r.ReadString();
                    replay.PartnerDeckNames[seat] = r.ReadString();
                    replay.Partners[seat] = new Deck { Name = replay.PartnerDeckNames[seat], Main = ReadCards(r), Extra = ReadCards(r) };
                }
            }
            replay.HumanSeat = r.ReadSByte();
            replay.Winner = r.ReadSByte();
            replay.Turns = r.ReadInt32();
            replay.Date = new DateTime(r.ReadInt64(), DateTimeKind.Utc).ToLocalTime();
            replay.CardCount = r.ReadInt32();
            var count = r.ReadInt32();
            for (int i = 0; i < count; i++)
                replay.Responses.Add(r.ReadBytes((int)ReadVarint(r)));
            if (version >= 3)
                for (int seat = 0; seat < 2; seat++)
                {
                    if (!r.ReadBoolean()) continue;
                    var looks = new CopyLook[r.ReadUInt16()];
                    for (int i = 0; i < looks.Length; i++)
                        looks[i] = new CopyLook((Rarity)r.ReadByte(), r.ReadByte(), r.ReadUInt32(), version >= 4 ? r.ReadByte() : 0);
                    replay.Looks[seat] = looks;
                }
            if (version >= 5)
                for (int seat = 0; seat < 2; seat++) replay.Mats[seat] = r.ReadString();
            return replay;
        }

        private static void WriteCards(BinaryWriter w, List<uint> cards)
        {
            w.Write((ushort)cards.Count);
            foreach (var c in cards) w.Write(c);
        }

        private static List<uint> ReadCards(BinaryReader r)
        {
            var list = new List<uint>();
            int n = r.ReadUInt16();
            for (int i = 0; i < n; i++) list.Add(r.ReadUInt32());
            return list;
        }

        private static void WriteVarint(BinaryWriter w, uint v)
        {
            while (v >= 0x80) { w.Write((byte)(v | 0x80)); v >>= 7; }
            w.Write((byte)v);
        }

        private static uint ReadVarint(BinaryReader r)
        {
            uint v = 0;
            for (int shift = 0; ; shift += 7)
            {
                var b = r.ReadByte();
                v |= (uint)(b & 0x7f) << shift;
                if (b < 0x80) return v;
            }
        }
    }

    /// <summary>The recorded answers, handed out in order to whichever seat the core asks.</summary>
    public sealed class ReplayFeed
    {
        private readonly IReadOnlyList<byte[]> _responses;
        public int Next { get; private set; }
        public bool Finished => Next >= _responses.Count;

        public ReplayFeed(IReadOnlyList<byte[]> responses) => _responses = responses;

        public byte[]? Take() => Finished ? null : _responses[Next++];
    }

    /// <summary>A seat played back from a replay. Once the answers run out it waits forever (end of the recording).</summary>
    public sealed class ReplayDuelist : IDuelist
    {
        private readonly ReplayFeed _feed;

        public ReplayDuelist(ReplayFeed feed) => _feed = feed;

        public byte[]? Respond(Prompt prompt, int attempt) => _feed.Take();

        public void OnPhaseChanged() { }
    }
}
