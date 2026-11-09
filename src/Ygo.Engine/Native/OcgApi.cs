using System;
using System.Runtime.InteropServices;

namespace Ygo.Engine.Native
{
    // Mirrors ygopro-core's ocgapi.h / ocgapi_types.h (API version 11.0).
    // Struct layouts must match the C definitions byte-for-byte.

    [StructLayout(LayoutKind.Sequential)]
    internal struct OcgCardData
    {
        public uint Code;
        public uint Alias;
        public IntPtr Setcodes; // uint16_t*, zero-terminated
        public uint Type;
        public uint Level;
        public uint Attribute;
        public ulong Race;
        public int Attack;
        public int Defense;
        public uint LScale;
        public uint RScale;
        public uint LinkMarker;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OcgPlayer
    {
        public uint StartingLP;
        public uint StartingDrawCount;
        public uint DrawCountPerTurn;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OcgDuelOptions
    {
        public ulong Seed0, Seed1, Seed2, Seed3;
        public ulong Flags;
        public OcgPlayer Team1;
        public OcgPlayer Team2;
        public IntPtr CardReader;
        public IntPtr Payload1;
        public IntPtr ScriptReader;
        public IntPtr Payload2;
        public IntPtr LogHandler;
        public IntPtr Payload3;
        public IntPtr CardReaderDone;
        public IntPtr Payload4;
        public byte EnableUnsafeLibraries;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OcgNewCardInfo
    {
        public byte Team;
        public byte Duelist;
        public uint Code;
        public byte Controller;
        public uint Location;
        public uint Sequence;
        public uint Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OcgQueryInfo
    {
        public uint Flags;
        public byte Controller;
        public uint Location;
        public uint Sequence;
        public uint OverlaySequence;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void OcgDataReader(IntPtr payload, uint code, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void OcgDataReaderDone(IntPtr payload, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int OcgScriptReader(IntPtr payload, IntPtr duel, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void OcgLogHandler(IntPtr payload, IntPtr text, int type);

    internal static class OcgApi
    {
        // Resolves to libocgcore.dylib (macOS), libocgcore.so (Linux), ocgcore.dll (Windows).
        private const string Lib = "ocgcore";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_GetVersion(out int major, out int minor);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int OCG_CreateDuel(out IntPtr duel, ref OcgDuelOptions options);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_DestroyDuel(IntPtr duel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_DuelNewCard(IntPtr duel, ref OcgNewCardInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_StartDuel(IntPtr duel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int OCG_DuelProcess(IntPtr duel);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr OCG_DuelGetMessage(IntPtr duel, out uint length);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void OCG_DuelSetResponse(IntPtr duel, byte[] buffer, uint length);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr OCG_DuelQueryLocation(IntPtr duel, out uint length, ref OcgQueryInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int OCG_LoadScript(IntPtr duel, byte[] buffer, uint length,
            [MarshalAs(UnmanagedType.LPStr)] string name);
    }

    /// <summary>
    /// IL2CPP (Unity) requires native callbacks to be static methods tagged with an attribute
    /// of this exact name. Defining it locally avoids a dependency on UnityEngine.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type delegateType) { DelegateType = delegateType; }
        public Type DelegateType { get; }
    }
}
