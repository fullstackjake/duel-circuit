using System;

namespace Ygo.Engine.Protocol
{
    /// <summary>Little-endian cursor over one core message payload.</summary>
    public sealed class MessageReader
    {
        private readonly byte[] _data;
        public int Offset { get; private set; }

        public MessageReader(byte[] data, int offset = 0)
        {
            _data = data;
            Offset = offset;
        }

        public bool AtEnd => Offset >= _data.Length;

        public void Seek(int offset) => Offset = offset;

        public byte U8() => _data[Offset++];

        public ushort U16() { var v = BitConverter.ToUInt16(_data, Offset); Offset += 2; return v; }
        public uint U32() { var v = BitConverter.ToUInt32(_data, Offset); Offset += 4; return v; }
        public int I32() { var v = BitConverter.ToInt32(_data, Offset); Offset += 4; return v; }
        public ulong U64() { var v = BitConverter.ToUInt64(_data, Offset); Offset += 8; return v; }

        /// <summary>The core's loc_info: controller u8, location u8, sequence u32, position u32.</summary>
        public CardRef LocInfo(uint code)
        {
            var controller = U8();
            var location = U8();
            var sequence = U32();
            var position = U32();
            return new CardRef(code, controller, location, sequence, position);
        }
    }

    /// <summary>A card as the core describes it in a message: its code plus where it is.</summary>
    public readonly struct CardRef
    {
        public readonly uint Code;
        public readonly byte Controller;
        public readonly uint Location;
        public readonly uint Sequence;
        public readonly uint Position;

        public CardRef(uint code, byte controller, uint location, uint sequence, uint position = 0)
        {
            Code = code;
            Controller = controller;
            Location = location;
            Sequence = sequence;
            Position = position;
        }
    }
}
