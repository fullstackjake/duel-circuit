using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// The "Import .ydk" button's file dialog (docs/dev/YDK-IMPORT-2026-11-07.md): macOS's own open panel (NSOpenPanel), called
    /// through the Objective-C runtime the way <see cref="WindowSize"/> and <see cref="ClickSupport"/> call AppKit (the
    /// app has no native plugin). It starts in ~/Downloads, shows .ydk files, picks one.
    /// <para>
    /// The panel runs modally on the main thread: the game waits (frozen, not hung) until the player picks or cancels, and
    /// the panel's own Cancel and Escape always end it. It never opens in an automated check run, in batch mode, off macOS,
    /// or while it's already open: <see cref="Pick"/> then returns null with the reason, and checks import from a path
    /// instead (DeckEditor.ImportYdkFile, RecipeBookScreen.ImportYdk). In the Unity editor it uses the editor's file panel.
    /// </para>
    /// </summary>
    public static class YdkFilePicker
    {
        private static bool _open;

        /// <summary>For checks: what the last <see cref="Pick"/> refused with ("" when it opened).</summary>
        public static string LastRefusal { get; private set; } = "";

        /// <summary>
        /// Shows the open panel and returns the chosen file's path; null when the player cancelled or the panel can't open
        /// (<paramref name="refused"/> says why; null for a plain cancel).
        /// </summary>
        public static string? Pick(out string? refused)
        {
            refused = Refusal();
            LastRefusal = refused ?? "";
            if (refused != null) return null;
            _open = true;
            try
            {
#if UNITY_EDITOR
                var path = UnityEditor.EditorUtility.OpenFilePanel("Import a .ydk deck list", Downloads, "ydk");
                return string.IsNullOrEmpty(path) ? null : path;
#else
                return Mac.RunOpenPanel(Downloads, out refused);
#endif
            }
            catch (Exception ex)
            {
                refused = $"the file dialog couldn't open ({ex.GetType().Name})";
                Debug.LogWarning($"Import .ydk: {ex}");
                return null;
            }
            finally
            {
                _open = false;
            }
        }

        /// <summary>Why the panel won't open now (null: it can).</summary>
        private static string? Refusal()
        {
            if (_open) return "the file dialog is already open";
            if (DuelController.IsCheckRun) return "automated checks don't open the file dialog";
            if (Application.isBatchMode) return "the file dialog needs a window";
#if !UNITY_EDITOR
            if (Application.platform != RuntimePlatform.OSXPlayer) return "the file dialog is only built for macOS";
#endif
            return null;
        }

        private static string Downloads
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                var downloads = Path.Combine(home, "Downloads");
                return Directory.Exists(downloads) ? downloads : home;
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

            private static IntPtr Sel(string name) => sel_registerName(name);

            private static IntPtr NSString(string text) => SendUtf8(objc_getClass("NSString"), Sel("stringWithUTF8String:"), text);

            public static string? RunOpenPanel(string folder, out string? refused)
            {
                refused = null;
                var panelClass = objc_getClass("NSOpenPanel");
                if (panelClass == IntPtr.Zero) { refused = "AppKit's open panel isn't available"; return null; }
                var panel = Send(panelClass, Sel("openPanel"));
                if (panel == IntPtr.Zero) { refused = "the open panel couldn't be made"; return null; }
                SendBool(panel, Sel("setCanChooseFiles:"), true);
                SendBool(panel, Sel("setCanChooseDirectories:"), false);
                SendBool(panel, Sel("setAllowsMultipleSelection:"), false);
                SendBool(panel, Sel("setResolvesAliases:"), true);
                SendPtr(panel, Sel("setTitle:"), NSString("Import a .ydk deck list"));
                SendPtr(panel, Sel("setMessage:"), NSString("Choose a .ydk file (deck sites and EDOPro export them)."));
                SendPtr(panel, Sel("setPrompt:"), NSString("Import"));
                var url = SendPtrBool(objc_getClass("NSURL"), Sel("fileURLWithPath:isDirectory:"), NSString(folder), true);
                if (url != IntPtr.Zero) SendPtr(panel, Sel("setDirectoryURL:"), url);
                // Only .ydk files: UTType (macOS 11+) when it's there, else the older file-type list.
                var utClass = objc_getClass("UTType");
                var ydkType = utClass != IntPtr.Zero ? SendPtr(utClass, Sel("typeWithFilenameExtension:"), NSString("ydk")) : IntPtr.Zero;
                if (ydkType != IntPtr.Zero && SendBoolPtr(panel, Sel("respondsToSelector:"), Sel("setAllowedContentTypes:")))
                    SendPtr(panel, Sel("setAllowedContentTypes:"), SendPtr(objc_getClass("NSArray"), Sel("arrayWithObject:"), ydkType));
                else
                    SendPtr(panel, Sel("setAllowedFileTypes:"), SendPtr(objc_getClass("NSArray"), Sel("arrayWithObject:"), NSString("ydk")));

                // Over the game's window even when it's raised (a full-screen window can sit above normal panels).
                var app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
                var window = app == IntPtr.Zero ? IntPtr.Zero : Send(app, Sel("mainWindow"));
                if (window != IntPtr.Zero && SendLong(window, Sel("level")) is long level && level > 0)
                    SendLongArg(panel, Sel("setLevel:"), level + 1);

                long response = SendLong(panel, Sel("runModal"));
                // The game's window takes the keys back.
                if (window != IntPtr.Zero) SendPtr(window, Sel("makeKeyAndOrderFront:"), IntPtr.Zero);
                if (response != ModalResponseOK) return null;
                var chosen = Send(panel, Sel("URL"));
                if (chosen == IntPtr.Zero) return null;
                var path = Send(chosen, Sel("path"));
                if (path == IntPtr.Zero) return null;
                var utf8 = Send(path, Sel("UTF8String"));
                return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
            }
        }
    }
}
