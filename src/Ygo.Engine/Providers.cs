using System.Collections.Generic;

namespace Ygo.Engine
{
    /// <summary>Static card data as stored in a .cdb card database.</summary>
    public sealed class CardInfo
    {
        public uint Code;
        public uint Alias;
        public ushort[] Setcodes = System.Array.Empty<ushort>();
        public uint Type;
        public uint Level;
        public uint Attribute;
        public ulong Race;
        public int Attack;
        public int Defense;
        public uint LScale;
        public uint RScale;
        public uint LinkMarker;
        /// <summary>The database's search categories (what the card does: search, banish, negate...); see CardTags.</summary>
        public ulong Category;
        public string Name = "";
        public string Text = "";
        /// <summary>Effect / prompt strings (str1..str16), referenced by descriptions as code &lt;&lt; 20 | index.</summary>
        public string[] Strings = System.Array.Empty<string>();

        public bool IsExtraDeckMonster => (Type & CardType.ExtraDeckTypes) != 0;
    }

    /// <summary>Supplies card data to the engine (SQLite in the harness; could be anything in Unity).</summary>
    public interface ICardDataProvider
    {
        CardInfo? Get(uint code);
        IEnumerable<CardInfo> All { get; }
    }

    /// <summary>Supplies Lua source for card and utility scripts, by file name (e.g. "c14558127.lua").</summary>
    public interface IScriptProvider
    {
        byte[]? Load(string name);
    }
}
