using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ygo.Engine.Native;

namespace Ygo.Engine
{
    public sealed class DuelSettings
    {
        public ulong Flags = DuelFlags.ModeMR5;
        public uint StartingLP = 8000;
        public uint StartingHand = 5;
        public uint DrawPerTurn = 1;
        public ulong Seed = 1;

        /// <summary>Who takes the first turn: 0 or 1 (the coin toss's winner's choice). Kept in <see cref="Flags"/>, so replays keep it.</summary>
        public int FirstPlayer
        {
            get => (Flags & DuelFlags.SecondPlayerStarts) != 0 ? 1 : 0;
            set => Flags = value == 1 ? Flags | DuelFlags.SecondPlayerStarts : Flags & ~DuelFlags.SecondPlayerStarts;
        }
    }

    /// <summary>
    /// One duel inside ocgcore. Owns the native handle and services the core's callbacks
    /// (card data, scripts, log output). Knows nothing about players or UI.
    /// </summary>
    public sealed class Duel : IDisposable
    {
        // Delegates are kept in static fields so the GC never collects them while native code holds the pointer.
        private static readonly OcgDataReader CardReaderDelegate = OnReadCard;
        private static readonly OcgDataReaderDone CardReaderDoneDelegate = OnReadCardDone;
        private static readonly OcgScriptReader ScriptReaderDelegate = OnReadScript;
        private static readonly OcgLogHandler LogHandlerDelegate = OnLog;

        private readonly ICardDataProvider _cards;
        private readonly IScriptProvider _scripts;
        private readonly Random _shuffleRng;
        private GCHandle _self;
        private IntPtr _handle;

        /// <summary>Raised for script errors and debug output from the core. Arguments: message, log type.</summary>
        public event Action<string, int>? Log;

        /// <summary>Scripts the core asked for that could not be found.</summary>
        public HashSet<string> MissingScripts { get; } = new HashSet<string>();

        public static Version CoreVersion
        {
            get
            {
                OcgApi.OCG_GetVersion(out var major, out var minor);
                return new Version(major, minor);
            }
        }

        /// <summary>Each player's Life Points at the start (the core doesn't announce them).</summary>
        public uint StartingLP { get; }

        public Duel(ICardDataProvider cards, IScriptProvider scripts, DuelSettings settings)
        {
            StartingLP = settings.StartingLP;
            _cards = cards;
            _scripts = scripts;
            _self = GCHandle.Alloc(this);
            var payload = GCHandle.ToIntPtr(_self);

            var seeds = SplitMix(settings.Seed);
            _shuffleRng = new Random(unchecked((int)seeds[0]));
            var player = new OcgPlayer
            {
                StartingLP = settings.StartingLP,
                StartingDrawCount = settings.StartingHand,
                DrawCountPerTurn = settings.DrawPerTurn,
            };
            var options = new OcgDuelOptions
            {
                Seed0 = seeds[0], Seed1 = seeds[1], Seed2 = seeds[2], Seed3 = seeds[3],
                Flags = settings.Flags,
                Team1 = player,
                Team2 = player,
                CardReader = Marshal.GetFunctionPointerForDelegate(CardReaderDelegate),
                Payload1 = payload,
                ScriptReader = Marshal.GetFunctionPointerForDelegate(ScriptReaderDelegate),
                Payload2 = payload,
                LogHandler = Marshal.GetFunctionPointerForDelegate(LogHandlerDelegate),
                Payload3 = payload,
                CardReaderDone = Marshal.GetFunctionPointerForDelegate(CardReaderDoneDelegate),
                Payload4 = payload,
            };

            var status = OcgApi.OCG_CreateDuel(out _handle, ref options);
            if (status != 0 || _handle == IntPtr.Zero)
            {
                _self.Free();
                throw new InvalidOperationException($"OCG_CreateDuel failed with status {status}");
            }

            // The core does not load the shared Lua libraries itself; every client loads these two first.
            foreach (var bootstrap in new[] { "constant.lua", "utility.lua" })
            {
                if (!LoadScript(bootstrap))
                    throw new InvalidOperationException($"Could not load {bootstrap}. Check the CardScripts path.");
            }
        }

        public void AddCard(byte team, uint code, uint location, uint position = Position.FaceDownDefense, byte duelist = 0, uint sequence = 0)
        {
            var info = new OcgNewCardInfo
            {
                Team = team,
                Duelist = duelist,
                Code = code,
                Controller = team,
                Location = location,
                Sequence = sequence,
                Position = position,
            };
            OcgApi.OCG_DuelNewCard(_handle, ref info);
        }

        /// <summary>
        /// Adds a deck, shuffling the Main Deck first: the core uses the order cards are added in. In a tag duel each team
        /// has a second duelist (<paramref name="duelist"/> 1) whose Deck, hand and Extra Deck swap in on the team's turns.
        /// <para>
        /// Each card is then stamped with <paramref name="deckId"/> and its slot (main, then extra, in the deck's own order)
        /// in the core's cover field, which follows the card wherever it goes (see <see cref="CopyLooks"/>). The shuffle
        /// draws the same random numbers as before, so duels and replays deal exactly as they did.
        /// </para>
        /// </summary>
        public void AddDeck(byte team, Deck deck, byte duelist = 0, int deckId = 0)
        {
            var order = new List<int>();
            for (int k = 0; k < deck.Main.Count; k++) order.Add(k);
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = _shuffleRng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            var covers = new List<uint>();
            foreach (var k in order)
            {
                AddCard(team, deck.Main[k], Location.Deck, duelist: duelist);
                covers.Add(CopyLooks.Cover(deckId, k));
            }
            for (int k = 0; k < deck.Extra.Count; k++)
            {
                AddCard(team, deck.Extra[k], Location.Extra, duelist: duelist);
                covers.Add(CopyLooks.Cover(deckId, deck.Main.Count + k));
            }
            _cardsAdded += covers.Count;
            StampCovers(team, covers);
        }

        private int _cardsAdded;

        /// <summary>
        /// Stamps the cards just added for a team, in the order they were added (card ids are handed out in order): every
        /// card of that team not stamped yet, which is exactly this deck, since each deck is stamped as it's added.
        /// </summary>
        private void StampCovers(byte team, List<uint> covers)
        {
            if (covers.Count == 0) return;
            var lua = new StringBuilder();
            lua.Append("local covers={").Append(string.Join(",", covers)).Append("} local n=0 ");
            lua.Append($"for id=1,{_cardsAdded + 16} do local c=Duel.GetCardFromCardID(id) ");
            lua.Append($"if c and c:GetOwner()=={team} and c:GetOriginalCode()~=0 and c:Cover()==0 then n=n+1 if covers[n] then c:Cover(covers[n]) end end end");
            RunLua("copy-covers", lua.ToString());
        }

        /// <summary>Runs a chunk of Lua in the duel (before it starts: setting up cards).</summary>
        public bool RunLua(string name, string source)
        {
            var bytes = Encoding.UTF8.GetBytes(source);
            return OcgApi.OCG_LoadScript(_handle, bytes, (uint)bytes.Length, name) != 0;
        }

        /// <summary>Which copy each card is (set from the recipe's decks); null when no deck has looks.</summary>
        public CopyLooks? Looks { get; set; }

        public void Start() => OcgApi.OCG_StartDuel(_handle);

        public DuelStatus Process() => (DuelStatus)OcgApi.OCG_DuelProcess(_handle);

        /// <summary>Returns every message the core produced since the last call, each as [type, payload...].</summary>
        public List<byte[]> GetMessages()
        {
            var result = new List<byte[]>();
            var ptr = OcgApi.OCG_DuelGetMessage(_handle, out var length);
            if (ptr == IntPtr.Zero || length == 0) return result;

            var buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, (int)length);
            int offset = 0;
            while (offset + 4 <= buffer.Length)
            {
                int size = BitConverter.ToInt32(buffer, offset);
                offset += 4;
                if (size <= 0 || offset + size > buffer.Length) break;
                var message = new byte[size];
                Buffer.BlockCopy(buffer, offset, message, 0, size);
                result.Add(message);
                offset += size;
            }
            return result;
        }

        /// <summary>
        /// Current cards in one location. Zone locations (MZone/SZone) return one entry per zone,
        /// null where the zone is empty; other locations return their cards in order.
        /// </summary>
        public List<FieldCard?> QueryLocation(byte player, uint location, uint flags = QueryParser.BoardFlags)
        {
            var info = new OcgQueryInfo { Flags = flags, Controller = player, Location = location };
            var ptr = OcgApi.OCG_DuelQueryLocation(_handle, out var length, ref info);
            if (ptr == IntPtr.Zero || length == 0) return new List<FieldCard?>();
            var buffer = new byte[length];
            Marshal.Copy(ptr, buffer, 0, (int)length);
            return QueryParser.ParseLocation(buffer);
        }

        public void SetResponse(byte[] response) =>
            OcgApi.OCG_DuelSetResponse(_handle, response, (uint)response.Length);

        public bool LoadScript(string name)
        {
            var source = _scripts.Load(name);
            if (source == null)
            {
                MissingScripts.Add(name);
                return false;
            }
            return OcgApi.OCG_LoadScript(_handle, source, (uint)source.Length, name) != 0;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                OcgApi.OCG_DestroyDuel(_handle);
                _handle = IntPtr.Zero;
            }
            if (_self.IsAllocated) _self.Free();
        }

        private static Duel FromPayload(IntPtr payload) => (Duel)GCHandle.FromIntPtr(payload).Target!;

        [MonoPInvokeCallback(typeof(OcgDataReader))]
        private static void OnReadCard(IntPtr payload, uint code, IntPtr dataPtr)
        {
            var duel = FromPayload(payload);
            var info = duel._cards.Get(code);
            var data = new OcgCardData { Code = code };
            if (info != null)
            {
                data.Alias = info.Alias;
                data.Type = info.Type;
                data.Level = info.Level;
                data.Attribute = info.Attribute;
                data.Race = info.Race;
                data.Attack = info.Attack;
                data.Defense = info.Defense;
                data.LScale = info.LScale;
                data.RScale = info.RScale;
                data.LinkMarker = info.LinkMarker;
            }

            // Zero-terminated uint16 array; freed in OnReadCardDone.
            var setcodes = info?.Setcodes ?? Array.Empty<ushort>();
            data.Setcodes = Marshal.AllocHGlobal((setcodes.Length + 1) * sizeof(ushort));
            for (int i = 0; i < setcodes.Length; i++)
                Marshal.WriteInt16(data.Setcodes, i * sizeof(ushort), (short)setcodes[i]);
            Marshal.WriteInt16(data.Setcodes, setcodes.Length * sizeof(ushort), 0);

            Marshal.StructureToPtr(data, dataPtr, false);
        }

        [MonoPInvokeCallback(typeof(OcgDataReaderDone))]
        private static void OnReadCardDone(IntPtr payload, IntPtr dataPtr)
        {
            var data = Marshal.PtrToStructure<OcgCardData>(dataPtr);
            if (data.Setcodes != IntPtr.Zero) Marshal.FreeHGlobal(data.Setcodes);
        }

        [MonoPInvokeCallback(typeof(OcgScriptReader))]
        private static int OnReadScript(IntPtr payload, IntPtr duelHandle, IntPtr namePtr)
        {
            var duel = FromPayload(payload);
            var name = Marshal.PtrToStringAnsi(namePtr) ?? "";
            return duel.LoadScript(name) ? 1 : 0;
        }

        [MonoPInvokeCallback(typeof(OcgLogHandler))]
        private static void OnLog(IntPtr payload, IntPtr textPtr, int type)
        {
            var duel = FromPayload(payload);
            duel.Log?.Invoke(Marshal.PtrToStringAnsi(textPtr) ?? "", type);
        }

        private static ulong[] SplitMix(ulong seed)
        {
            var result = new ulong[4];
            for (int i = 0; i < 4; i++)
            {
                seed += 0x9E3779B97F4A7C15UL;
                ulong z = seed;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                result[i] = z ^ (z >> 31);
            }
            return result;
        }
    }
}
