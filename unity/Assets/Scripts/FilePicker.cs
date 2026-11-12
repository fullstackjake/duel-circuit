using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The game's open-file dialog (docs/dev/CARD-IMPORT-SPEC-2026-11-12.md, part B): the system's own dialog, with no
    /// native plugin. Windows: comdlg32's GetOpenFileNameW. macOS: NSOpenPanel, called through the Objective-C runtime the
    /// way <see cref="WindowSize"/> and <see cref="ClickSupport"/> call AppKit. The Unity editor: the editor's file panel
    /// (one file).
    /// <para>
    /// The dialog runs modally on the main thread: the game waits (frozen, not hung) until the player picks or cancels.
    /// It never opens in an automated check run, in batch mode, on another platform, or while it's already open:
    /// <see cref="Pick"/> then returns null with the reason, and checks hand their paths in instead.
    /// </para>
    /// </summary>
    public static class FilePicker
    {
        /// <summary>What to ask for.</summary>
        public sealed class Request
        {
            public string Title = "", Message = "", Prompt = "Open";
            /// <summary>The filter's name ("Card pictures and zips").</summary>
            public string FilterName = "Files";
            /// <summary>The extensions shown, without dots ("jpg", "zip"). Empty: every file.</summary>
            public string[] Extensions = Array.Empty<string>();
            /// <summary>Whether the player may pick several files.</summary>
            public bool Multiple;
            /// <summary>Where the dialog starts. Null (or missing): Downloads, else home.</summary>
            public string? StartFolder;
        }

        private static bool _open;

        /// <summary>For checks: what the last <see cref="Pick"/> refused with ("" when it opened).</summary>
        public static string LastRefusal { get; private set; } = "";

        /// <summary>
        /// Shows the dialog and returns the chosen files (full paths); null when the player cancelled or the dialog can't
        /// open (<paramref name="refused"/> says why; null for a plain cancel).
        /// </summary>
        public static string[]? Pick(Request request, out string? refused)
        {
            refused = Refusal();
            LastRefusal = refused ?? "";
            if (refused != null) return null;
            _open = true;
            try
            {
                var folder = StartFolder(request.StartFolder);
#if UNITY_EDITOR
                var filters = new[] { request.FilterName, request.Extensions.Length > 0 ? string.Join(",", request.Extensions) : "*" };
                var path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(request.Title, folder, filters);
                return string.IsNullOrEmpty(path) ? null : new[] { path };
#else
                if (Application.platform == RuntimePlatform.WindowsPlayer) return Win.Open(request, folder, out refused);
                return Mac.RunOpenPanel(request, folder, out refused);
#endif
            }
            catch (Exception ex)
            {
                refused = $"the file dialog couldn't open ({ex.GetType().Name})";
                Debug.LogWarning($"File dialog: {ex}");
                return null;
            }
            finally
            {
                _open = false;
            }
        }

        /// <summary>Why the dialog won't open now (null: it can).</summary>
        private static string? Refusal()
        {
            if (_open) return "the file dialog is already open";
            if (DuelController.IsCheckRun) return "automated checks don't open the file dialog";
            if (Application.isBatchMode) return "the file dialog needs a window";
#if !UNITY_EDITOR
            if (Application.platform != RuntimePlatform.OSXPlayer && Application.platform != RuntimePlatform.WindowsPlayer)
                return "no file dialog on this platform";
#endif
            return null;
        }

        private static string StartFolder(string? asked)
        {
            if (!string.IsNullOrEmpty(asked) && Directory.Exists(asked)) return asked!;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home)) home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            var downloads = Path.Combine(home, "Downloads");
            return Directory.Exists(downloads) ? downloads : home;
        }

        /// <summary>
        /// The Windows filter string: "Name (*.a;*.b)\0*.a;*.b\0\0". No extensions: every file.
        /// </summary>
        internal static string FilterString(string name, string[] extensions)
        {
            var patterns = new List<string>();
            foreach (var ext in extensions)
                if (!string.IsNullOrWhiteSpace(ext)) patterns.Add("*." + ext.Trim().TrimStart('.'));
            var joined = patterns.Count > 0 ? string.Join(";", patterns) : "*.*";
            return $"{name} ({joined})\0{joined}\0\0";
        }

        /// <summary>
        /// The files in a GetOpenFileNameW buffer, given its content up to the double null. One file comes back as its
        /// full path alone; several as the folder, then each name ("C:\dir\0a.jpg\0b.jpg"). A drive's root already ends
        /// with a separator ("C:\").
        /// </summary>
        internal static string[] ParseMultiSelect(string raw)
        {
            var parts = new List<string>();
            foreach (var part in raw.Split('\0'))
                if (part.Length > 0) parts.Add(part);
            if (parts.Count <= 1) return parts.ToArray();
            var dir = parts[0];
            var sep = dir.EndsWith("\\") || dir.EndsWith("/") ? "" : "\\";
            var files = new string[parts.Count - 1];
            for (int i = 1; i < parts.Count; i++) files[i - 1] = dir + sep + parts[i];
            return files;
        }

        /// <summary>The same, from the whole buffer: everything before the first double null (or the end).</summary>
        internal static string[] ParseMultiSelect(char[] buffer)
        {
            if (buffer.Length == 0 || buffer[0] == '\0') return Array.Empty<string>();
            int end = 0;
            while (end < buffer.Length && !(buffer[end] == '\0' && (end + 1 >= buffer.Length || buffer[end + 1] == '\0')))
                end++;
            return ParseMultiSelect(new string(buffer, 0, end));
        }

        /// <summary>comdlg32's open dialog (Windows player only).</summary>
        internal static class Win
        {
            private const int BufferChars = 65536;
            private const uint FNERR_BUFFERTOOSMALL = 0x3003;
            private const int OFN_HIDEREADONLY = 0x4, OFN_NOCHANGEDIR = 0x8, OFN_ALLOWMULTISELECT = 0x200,
                OFN_PATHMUSTEXIST = 0x800, OFN_FILEMUSTEXIST = 0x1000, OFN_EXPLORER = 0x80000;

            /// <summary>OPENFILENAMEW (152 bytes on 64-bit). The strings are unmanaged memory we own.</summary>
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            internal struct OPENFILENAMEW
            {
                public int lStructSize;
                public IntPtr hwndOwner;
                public IntPtr hInstance;
                public IntPtr lpstrFilter;
                public IntPtr lpstrCustomFilter;
                public int nMaxCustFilter;
                public int nFilterIndex;
                public IntPtr lpstrFile;
                public int nMaxFile;
                public IntPtr lpstrFileTitle;
                public int nMaxFileTitle;
                public IntPtr lpstrInitialDir;
                public IntPtr lpstrTitle;
                public int Flags;
                public short nFileOffset;
                public short nFileExtension;
                public IntPtr lpstrDefExt;
                public IntPtr lCustData;
                public IntPtr lpfnHook;
                public IntPtr lpTemplateName;
                public IntPtr pvReserved;
                public int dwReserved;
                public int FlagsEx;
            }

            [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] private static extern bool GetOpenFileNameW(ref OPENFILENAMEW ofn);
            [DllImport("comdlg32.dll")] private static extern uint CommDlgExtendedError();
            [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();

            public static string[]? Open(Request request, string folder, out string? refused)
            {
                refused = null;
                IntPtr file = IntPtr.Zero, filter = IntPtr.Zero, dir = IntPtr.Zero, title = IntPtr.Zero;
                try
                {
                    int bytes = BufferChars * 2;
                    file = Marshal.AllocHGlobal(bytes);
                    Marshal.Copy(new byte[bytes], 0, file, bytes);
                    filter = Marshal.StringToHGlobalUni(FilterString(request.FilterName, request.Extensions));
                    dir = Marshal.StringToHGlobalUni(folder);
                    if (request.Title.Length > 0) title = Marshal.StringToHGlobalUni(request.Title);

                    var ofn = new OPENFILENAMEW
                    {
                        lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                        hwndOwner = GetActiveWindow(),
                        lpstrFilter = filter,
                        nFilterIndex = 1,
                        lpstrFile = file,
                        nMaxFile = BufferChars,
                        lpstrInitialDir = dir,
                        lpstrTitle = title,
                        // The game's working folder must not move: OFN_NOCHANGEDIR.
                        Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_HIDEREADONLY | OFN_NOCHANGEDIR
                                | (request.Multiple ? OFN_ALLOWMULTISELECT : 0),
                    };
                    if (!GetOpenFileNameW(ref ofn))
                    {
                        uint code = CommDlgExtendedError();
                        if (code == 0) return null;
                        refused = code == FNERR_BUFFERTOOSMALL
                            ? "too many files at once: pick fewer, or zip them"
                            : $"the file dialog failed (error 0x{code:X4})";
                        return null;
                    }
                    var chars = new char[BufferChars];
                    Marshal.Copy(file, chars, 0, BufferChars);
                    var files = ParseMultiSelect(chars);
                    return files.Length > 0 ? files : null;
                }
                finally
                {
                    if (file != IntPtr.Zero) Marshal.FreeHGlobal(file);
                    if (filter != IntPtr.Zero) Marshal.FreeHGlobal(filter);
                    if (dir != IntPtr.Zero) Marshal.FreeHGlobal(dir);
                    if (title != IntPtr.Zero) Marshal.FreeHGlobal(title);
                }
            }
        }

        /// <summary>NSOpenPanel through the Objective-C runtime (macOS player only).</summary>
        private static class Mac
        {
            private const string Objc = "/usr/lib/libobjc.A.dylib";
            private const long ModalResponseOK = 1;

            [DllImport(Objc)] private static extern IntPtr objc_getClass(string name);
            [DllImport(Objc)] private static extern IntPtr sel_registerName(string name);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPtrBool(IntPtr receiver, IntPtr selector, IntPtr arg, [MarshalAs(UnmanagedType.I1)] bool flag);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool flag);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern long SendLong(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern void SendLongArg(IntPtr receiver, IntPtr selector, long value);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendUtf8(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBoolPtr(IntPtr receiver, IntPtr selector, IntPtr arg);
            // NSUInteger in and out: -count, -objectAtIndex:.
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern ulong SendULong(IntPtr receiver, IntPtr selector);
            [DllImport(Objc, EntryPoint = "objc_msgSend")] private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, ulong index);

            private static IntPtr Sel(string name) => sel_registerName(name);

            private static IntPtr NSString(string text) => SendUtf8(objc_getClass("NSString"), Sel("stringWithUTF8String:"), text);

            public static string[]? RunOpenPanel(Request request, string folder, out string? refused)
            {
                refused = null;
                var panelClass = objc_getClass("NSOpenPanel");
                if (panelClass == IntPtr.Zero) { refused = "AppKit's open panel isn't available"; return null; }
                var panel = Send(panelClass, Sel("openPanel"));
                if (panel == IntPtr.Zero) { refused = "the open panel couldn't be made"; return null; }
                SendBool(panel, Sel("setCanChooseFiles:"), true);
                SendBool(panel, Sel("setCanChooseDirectories:"), false);
                SendBool(panel, Sel("setAllowsMultipleSelection:"), request.Multiple);
                SendBool(panel, Sel("setResolvesAliases:"), true);
                if (request.Title.Length > 0) SendPtr(panel, Sel("setTitle:"), NSString(request.Title));
                if (request.Message.Length > 0) SendPtr(panel, Sel("setMessage:"), NSString(request.Message));
                if (request.Prompt.Length > 0) SendPtr(panel, Sel("setPrompt:"), NSString(request.Prompt));
                var url = SendPtrBool(objc_getClass("NSURL"), Sel("fileURLWithPath:isDirectory:"), NSString(folder), true);
                if (url != IntPtr.Zero) SendPtr(panel, Sel("setDirectoryURL:"), url);
                AllowTypes(panel, request.Extensions);

                // Over the game's window even when it's raised (a full-screen window can sit above normal panels).
                var app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
                var window = app == IntPtr.Zero ? IntPtr.Zero : Send(app, Sel("mainWindow"));
                if (window != IntPtr.Zero && SendLong(window, Sel("level")) is long level && level > 0)
                    SendLongArg(panel, Sel("setLevel:"), level + 1);

                long response = SendLong(panel, Sel("runModal"));
                // The game's window takes the keys back.
                if (window != IntPtr.Zero) SendPtr(window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
                if (response != ModalResponseOK) return null;

                var paths = new List<string>();
                var urls = Send(panel, Sel("URLs"));
                if (urls != IntPtr.Zero)
                {
                    ulong count = SendULong(urls, Sel("count"));
                    for (ulong i = 0; i < count; i++)
                        if (PathOf(SendIndex(urls, Sel("objectAtIndex:"), i)) is string p) paths.Add(p);
                }
                // The single URL, as before, if the list came back empty.
                if (paths.Count == 0 && PathOf(Send(panel, Sel("URL"))) is string one) paths.Add(one);
                return paths.Count > 0 ? paths.ToArray() : null;
            }

            /// <summary>Only these extensions: UTType (macOS 11+) when it's there, else the older file-type list.</summary>
            private static void AllowTypes(IntPtr panel, string[] extensions)
            {
                if (extensions.Length == 0) return;
                var utClass = objc_getClass("UTType");
                if (utClass != IntPtr.Zero && SendBoolPtr(panel, Sel("respondsToSelector:"), Sel("setAllowedContentTypes:")))
                {
                    var types = Send(objc_getClass("NSMutableArray"), Sel("array"));
                    int added = 0;
                    foreach (var ext in extensions)
                    {
                        var type = SendPtr(utClass, Sel("typeWithFilenameExtension:"), NSString(ext));
                        if (type == IntPtr.Zero) continue;
                        SendPtr(types, Sel("addObject:"), type);
                        added++;
                    }
                    if (types != IntPtr.Zero && added > 0)
                    {
                        SendPtr(panel, Sel("setAllowedContentTypes:"), types);
                        return;
                    }
                }
                var names = Send(objc_getClass("NSMutableArray"), Sel("array"));
                if (names == IntPtr.Zero) return;
                foreach (var ext in extensions) SendPtr(names, Sel("addObject:"), NSString(ext));
                SendPtr(panel, Sel("setAllowedFileTypes:"), names);
            }

            private static string? PathOf(IntPtr url)
            {
                if (url == IntPtr.Zero) return null;
                var path = Send(url, Sel("path"));
                if (path == IntPtr.Zero) return null;
                var utf8 = Send(path, Sel("UTF8String"));
                return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
            }
        }
    }
}
