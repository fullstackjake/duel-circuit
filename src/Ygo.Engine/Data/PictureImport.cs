using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ygo.Harness")]

namespace Ygo.Engine.Data
{
    /// <summary>Where imported pictures go: card pictures, high-resolution card pictures, or the card back.</summary>
    public enum PictureKind { Standard, Hd, Back }

    /// <summary>
    /// What an import would do, from <see cref="PictureImport.Scan"/>: counts for the screen's one question, and the
    /// pictures <see cref="PictureImport.Run"/> will write. A plan with a <see cref="Problem"/> can't run.
    /// </summary>
    public sealed class PictureImportPlan
    {
        /// <summary>Where the pictures go.</summary>
        public PictureKind Kind { get; }
        /// <summary>The full path of images/ or hd/; for <see cref="PictureKind.Back"/>, the cards/ folder.</summary>
        public string TargetFolder { get; }
        /// <summary>Pictures that would be written (New + Existing).</summary>
        public int Total => Items.Count;
        /// <summary>No picture of that card in the target yet.</summary>
        public int New { get; }
        /// <summary>A picture of that card is already there (under any of the three extensions).</summary>
        public int Existing { get; }
        /// <summary>Files not imported (bad name, not a picture, duplicate, too big, unreadable); see SkippedExamples.</summary>
        public int Skipped { get; }
        /// <summary>Up to 5 skipped files, as "name: why".</summary>
        public IReadOnlyList<string> SkippedExamples { get; }
        /// <summary>Why the whole import can't run (unreadable zip, no pictures, a missing file...): show it, stop. Null when it can.</summary>
        public string? Problem { get; }

        internal IReadOnlyList<PictureImport.Item> Items { get; }

        internal PictureImportPlan(PictureKind kind, string targetFolder, List<PictureImport.Item> items, int existing, int skipped,
            List<string> skippedExamples, string? problem)
        {
            Kind = kind;
            TargetFolder = targetFolder;
            Items = problem == null ? items : new List<PictureImport.Item>();
            Existing = problem == null ? existing : 0;
            New = Items.Count - Existing;
            Skipped = skipped;
            SkippedExamples = skippedExamples;
            Problem = problem;
        }
    }

    /// <summary>What <see cref="PictureImport.Run"/> did. Added + Replaced + Kept + Failed is the plan's Total unless Cancelled.</summary>
    public sealed class PictureImportResult
    {
        public int Added, Replaced, Kept, Failed;
        public bool Cancelled;
        /// <summary>Up to 5 failed pictures, as "name: why".</summary>
        public IReadOnlyList<string> FailedExamples => failedExamples;
        /// <summary>Full paths written (added or replaced): the game forgets what it had cached for these.</summary>
        public IReadOnlyList<string> WrittenFiles => writtenFiles;
        /// <summary>Full paths deleted (the card's picture under another extension or another number spelling, or the other card back).</summary>
        public IReadOnlyList<string> RemovedFiles => removedFiles;

        internal readonly List<string> failedExamples = new List<string>();
        internal readonly List<string> writtenFiles = new List<string>();
        internal readonly List<string> removedFiles = new List<string>();

        internal void Fail(string name, string why)
        {
            Failed++;
            if (failedExamples.Count < PictureImport.MaxExamples) failedExamples.Add($"{name}: {why}");
        }
    }

    /// <summary>
    /// Imports card pictures from a zip or loose image files into the game's mods folder (docs/dev/CARD-IMPORT-SPEC-2026-11-12.md,
    /// Part A). A card picture is named by its passcode (<c>89631139.jpg</c>) and written as the game looks it up (the number
    /// without leading zeros); folders in a zip are flattened, and an entry's name is only ever checked, never used as a path.
    /// Each picture is written to <c>name.importing</c> and then moved into place; a picture it replaces is first renamed to
    /// <c>name.replacing</c> and only deleted once the new one is in, so a cancelled or failed import leaves the old picture
    /// and nothing half-written. No state is kept (only <see cref="Hooks"/>, null outside tests): safe on a background thread.
    /// </summary>
    public static class PictureImport
    {
        /// <summary>The biggest picture taken (uncompressed); reading stops there whatever a zip's header says.</summary>
        public const long MaxPictureBytes = 64L * 1024 * 1024;
        internal const int MaxExamples = 5;
        const string TempSuffix = ".importing";
        const string BackupSuffix = ".replacing";
        const string OldStays = "the old picture can't be replaced";
        const string DiskFull = "the disk is full";
        static readonly string[] Extensions = { "jpg", "jpeg", "png" };

        enum PictureType { None, Png, Jpeg }

        /// <summary>Test seams (the harness's --import-test); null in the game.</summary>
        internal static class Hooks
        {
            /// <summary>Free bytes on the target's drive, instead of DriveInfo (null: unknown).</summary>
            internal static Func<string, long?>? FreeSpace;
            /// <summary>Called instead of File.Move(from, to), e.g. to fail a move.</summary>
            internal static Action<string, string>? Move;
        }

        /// <summary>One picture to write: from a loose file (Entry -1) or a zip entry (by its index, checked by its full name).</summary>
        internal sealed class Item
        {
            public string Source = "";      // the loose file or the zip
            public int Entry = -1;
            public string EntryName = "";
            public string Display = "";     // for messages: the file's name (never the path inside the zip)
            public string Card = "";        // the card number as the game writes it ("back" for a card back)
            public string TargetName = "";  // validated: <uint>.<jpg|jpeg|png>, or back.png / back.jpg
            public long Length;             // the declared size
        }

        /// <summary>
        /// Looks at what <paramref name="paths"/> hold (one .zip, image files, or a mix) without writing anything. Reads zip
        /// directories and each file's first bytes only. <paramref name="targetFolder"/> is images/ or hd/, or for
        /// <see cref="PictureKind.Back"/> the cards/ folder; it needn't exist yet.
        /// </summary>
        public static PictureImportPlan Scan(PictureKind kind, IReadOnlyList<string> paths, string targetFolder)
        {
            var target = Path.GetFullPath(targetFolder);
            var items = new List<Item>();
            var skippedExamples = new List<string>();
            int skipped = 0;
            void Skip(string name, string why)
            {
                skipped++;
                if (skippedExamples.Count < MaxExamples) skippedExamples.Add($"{name}: {why}");
            }
            PictureImportPlan Stop(string problem) => new PictureImportPlan(kind, target, items, 0, skipped, skippedExamples, problem);

            if (paths == null || paths.Count == 0) return Stop("No files were picked.");
            foreach (var path in paths)
                if (!File.Exists(path)) return Stop($"{Path.GetFileName(path)} can't be found.");

            if (kind == PictureKind.Back)
            {
                if (paths.Count != 1 || IsZip(paths[0])) return Stop("Pick one picture for the card back.");
                var path = paths[0];
                var name = Path.GetFileName(path);
                string? why = null;
                var type = PictureType.None;
                long length = 0;
                try
                {
                    if ((length = new FileInfo(path).Length) > MaxPictureBytes) why = "too big";
                    else
                        using (var s = File.OpenRead(path))
                            if ((type = ReadType(s)) == PictureType.None) why = "not a picture";
                }
                catch (Exception e) when (IsIo(e)) { why = "can't be read"; }
                if (why != null)
                {
                    Skip(name, why);
                    return Stop($"{name} isn't a picture the game can read. Use a .png or .jpg.");
                }
                items.Add(new Item { Source = path, Display = name, Card = "back", TargetName = type == PictureType.Png ? "back.png" : "back.jpg", Length = length });
                if (NoRoom(target, items) is { } room) return Stop(room);
                int existing = ExistingCards(target, kind).ContainsKey("back") ? 1 : 0;
                return new PictureImportPlan(kind, target, items, existing, skipped, skippedExamples, null);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Consider(string fullName, string fileName, long length, Func<Stream> open, Action<Item> fill)
            {
                if (SilentlySkipped(fullName, fileName)) return;
                if (!CardPictureName(fileName, out var code, out var ext)) { Skip(fileName, "not a card number"); return; }
                var card = code.ToString(CultureInfo.InvariantCulture);
                if (seen.Contains(card)) { Skip(fileName, "duplicate"); return; }
                if (length > MaxPictureBytes) { Skip(fileName, "too big"); return; }
                try
                {
                    using var s = open();
                    if (ReadType(s) == PictureType.None) { Skip(fileName, "not a picture"); return; }
                }
                catch (Exception e) when (IsIo(e) || e is NotSupportedException) { Skip(fileName, "can't be read"); return; }
                seen.Add(card);
                var item = new Item { Display = fileName, Card = card, TargetName = card + "." + ext, Length = length };
                fill(item);
                items.Add(item);
            }

            foreach (var path in paths)
            {
                if (IsZip(path))
                {
                    try
                    {
                        using var zip = ZipFile.OpenRead(path);
                        var entries = zip.Entries;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var entry = entries[i];
                            int index = i;
                            Consider(entry.FullName, FileNamePart(entry.FullName), entry.Length, entry.Open,
                                item => { item.Source = path; item.Entry = index; item.EntryName = entry.FullName; });
                        }
                    }
                    catch (Exception e) when (IsIo(e) || e is NotSupportedException)
                    {
                        return Stop($"{Path.GetFileName(path)} can't be read. It may be damaged, or not fully downloaded.");
                    }
                }
                else
                {
                    var name = Path.GetFileName(path);
                    long length;
                    try { length = new FileInfo(path).Length; }
                    catch (Exception e) when (IsIo(e)) { Skip(name, "can't be read"); continue; }
                    Consider(name, name, length, () => File.OpenRead(path), item => item.Source = path);
                }
            }

            if (items.Count == 0)
                return Stop(paths.Count == 1
                    ? $"There are no card pictures in {Path.GetFileName(paths[0])}. Card pictures are named by card number, like 89631139.jpg."
                    : "None of these files is a card picture. Card pictures are named by card number, like 89631139.jpg.");
            if (NoRoom(target, items) is { } noRoom) return Stop(noRoom);
            var there = ExistingCards(target, kind);
            int existingCount = 0;
            foreach (var item in items)
                if (there.ContainsKey(item.Card)) existingCount++;
            return new PictureImportPlan(kind, target, items, existingCount, skipped, skippedExamples, null);
        }

        /// <summary>The Problem when the pictures' declared sizes add up to more than the drive has free; null when they fit or it can't tell.</summary>
        static string? NoRoom(string target, List<Item> items)
        {
            long need = 0;
            foreach (var item in items) need += item.Length;
            long? free;
            if (Hooks.FreeSpace is { } hook) free = hook(target);
            else
                try
                {
                    var root = Path.GetPathRoot(target);
                    free = string.IsNullOrEmpty(root) ? (long?)null : new DriveInfo(root).AvailableFreeSpace;
                }
                catch (Exception e) when (IsIo(e) || e is InvalidOperationException) { free = null; }
            if (free == null || need <= free.Value) return null;
            long mb = Math.Max(1, (need + 1024 * 1024 - 1) / (1024 * 1024));
            var size = mb >= 1024 ? (mb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : mb + " MB";
            return $"There isn't enough room on the disk for these pictures. They need about {size}.";
        }

        /// <summary>
        /// Writes the plan's pictures. With <paramref name="replaceExisting"/>, a card already there is overwritten and its
        /// picture under another extension deleted; without it, the card is left as it is (Kept). One failure doesn't stop
        /// the rest, except a full disk, which fails the rest at once; <paramref name="cancel"/> stops between pictures.
        /// <paramref name="progress"/> gets (done, total) at most about 20 times a second, plus the start and the end, on the
        /// calling thread.
        /// </summary>
        public static PictureImportResult Run(PictureImportPlan plan, bool replaceExisting,
            IProgress<(int done, int total)>? progress = null, CancellationToken cancel = default)
        {
            var result = new PictureImportResult();
            if (plan.Problem != null || plan.Items.Count == 0) return result;
            var target = plan.TargetFolder;
            int total = plan.Items.Count;

            try
            {
                Directory.CreateDirectory(target);
            }
            catch (Exception e) when (IsIo(e))
            {
                foreach (var item in plan.Items) result.Fail(item.Display, "the folder can't be written to");
                return result;
            }
            RemoveStaleFiles(target);
            var there = ExistingCards(target, plan.Kind);

            var zips = new Dictionary<string, ZipArchive?>(StringComparer.Ordinal);
            var buffer = new byte[81920];
            var clock = Stopwatch.StartNew();
            long lastReport = long.MinValue / 2;
            progress?.Report((0, total));
            try
            {
                int done = 0;
                bool full = false;
                foreach (var item in plan.Items)
                {
                    if (full) { result.Fail(item.Display, DiskFull); done++; continue; }
                    if (cancel.IsCancellationRequested) { result.Cancelled = true; break; }
                    full = Import(item, target, replaceExisting, there, zips, buffer, result);
                    done++;
                    long now = clock.ElapsedMilliseconds;
                    if (done == total || now - lastReport >= 50)
                    {
                        lastReport = now;
                        progress?.Report((done, total));
                    }
                }
                if (full) progress?.Report((total, total));
            }
            finally
            {
                foreach (var zip in zips.Values) zip?.Dispose();
            }
            return result;
        }

        static void Move(string from, string to)
        {
            if (Hooks.Move is { } hook) hook(from, to);
            else File.Move(from, to);
        }

        /// <summary>Imports one picture; true when the disk turned out to be full (the caller stops there).</summary>
        static bool Import(Item item, string target, bool replaceExisting, Dictionary<string, List<string>> there,
            Dictionary<string, ZipArchive?> zips, byte[] buffer, PictureImportResult result)
        {
            // Every file of this card already in the target (any extension, any leading zeros; for a back, png and jpg).
            var olds = there.TryGetValue(item.Card, out var list) ? list : new List<string>();
            bool existed = olds.Count > 0;
            if (existed && !replaceExisting) { result.Kept++; return false; }

            var dest = Path.Combine(target, item.TargetName);
            var temp = dest + TempSuffix;
            var backups = new List<(string old, string backup)>();
            try
            {
                using (var source = OpenSource(item, zips, out long declared, out uint crc))
                {
                    if (declared > MaxPictureBytes) throw new ImportFailure("too big");
                    using var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                    long written = 0;
                    uint running = 0xFFFFFFFFu;
                    int headerFill = 0;
                    var header = new byte[8];
                    int n;
                    while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (written + n > MaxPictureBytes) throw new ImportFailure("too big");
                        for (int i = 0; i < n && headerFill < header.Length; i++) header[headerFill++] = buffer[i];
                        output.Write(buffer, 0, n);
                        if (declared >= 0) running = Crc32(running, buffer, n);
                        written += n;
                    }
                    var type = TypeOf(header, headerFill);
                    if (type == PictureType.None) throw new ImportFailure("not a picture");
                    if (item.Card == "back" && (type == PictureType.Png) != item.TargetName.EndsWith(".png", StringComparison.Ordinal))
                        throw new ImportFailure("the file has changed");
                    // Some runtimes stop at the declared size, so a short or lying header only shows in the checksum.
                    if (declared >= 0 && (written != declared || ~running != crc)) throw new ImportFailure("damaged in the zip");
                    output.Flush();
                }

                // A read-only old picture could be renamed but never deleted: leave it alone.
                foreach (var old in olds)
                    if ((File.GetAttributes(old) & FileAttributes.ReadOnly) != 0) throw new ImportFailure(OldStays);
                // The old pictures step aside (renamed, not deleted), the new one moves in, and only then do they go.
                try
                {
                    foreach (var old in olds)
                    {
                        var backup = old + BackupSuffix;
                        if (File.Exists(backup)) File.Delete(backup);
                        Move(old, backup);
                        backups.Add((old, backup));
                    }
                }
                catch (Exception e) when (IsIo(e) && !IsDiskFull(e)) { Restore(backups); throw new ImportFailure(OldStays); }
                catch (Exception e) when (IsIo(e)) { Restore(backups); throw; }
                try { Move(temp, dest); }
                catch (Exception e) when (IsIo(e)) { Restore(backups); throw; }
                for (int i = 0; i < backups.Count; i++)
                {
                    try { File.Delete(backups[i].backup); }
                    catch (Exception e) when (IsIo(e))
                    {
                        // Can't finish: take the new picture back out and put back what's left of the old ones.
                        try { File.Delete(dest); } catch (Exception e2) when (IsIo(e2)) { }
                        Restore(backups.GetRange(i, backups.Count - i));
                        throw new ImportFailure(OldStays);
                    }
                }

                result.writtenFiles.Add(dest);
                foreach (var old in olds)
                    if (!string.Equals(old, dest, StringComparison.Ordinal)) result.removedFiles.Add(old);
                there[item.Card] = new List<string> { dest };
                if (existed) result.Replaced++;
                else result.Added++;
                return false;
            }
            catch (ImportFailure f) { result.Fail(item.Display, f.Message); return false; }
            catch (Exception e) when (IsIo(e) || e is NotSupportedException)
            {
                bool full = IsDiskFull(e);
                result.Fail(item.Display, full ? DiskFull : Reason(e));
                return full;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception e) when (IsIo(e)) { }
            }
        }

        /// <summary>Renames backups to their old names again (best effort: one that can't be is restored by the next Run).</summary>
        static void Restore(List<(string old, string backup)> backups)
        {
            foreach (var (old, backup) in backups)
            {
                try
                {
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(backup, old);
                }
                catch (Exception e) when (IsIo(e)) { }
            }
        }

        static bool IsDiskFull(Exception e)
        {
            if (!(e is IOException)) return false;
            int h = e.HResult;
            return h == unchecked((int)0x80070070) || h == unchecked((int)0x80070027) || h == 28; // Windows; ENOSPC elsewhere
        }

        /// <summary>The item's bytes; <paramref name="declared"/> and <paramref name="crc"/> are a zip entry's stated size and CRC-32 (-1 and 0 for a loose file).</summary>
        static Stream OpenSource(Item item, Dictionary<string, ZipArchive?> zips, out long declared, out uint crc)
        {
            declared = -1;
            crc = 0;
            if (item.Entry < 0) return File.OpenRead(item.Source);
            if (!zips.TryGetValue(item.Source, out var zip))
            {
                try { zip = ZipFile.OpenRead(item.Source); }
                catch (Exception e) when (IsIo(e) || e is NotSupportedException) { zip = null; }
                zips[item.Source] = zip;
            }
            if (zip == null) throw new ImportFailure("the zip can't be read");
            var entries = zip.Entries;
            if (item.Entry >= entries.Count || entries[item.Entry].FullName != item.EntryName)
                throw new ImportFailure("the zip has changed");
            var entry = entries[item.Entry];
            declared = entry.Length;
            crc = entry.Crc32;
            return entry.Open();
        }

        static string Reason(Exception e) => e switch
        {
            UnauthorizedAccessException _ => "the folder can't be written to",
            FileNotFoundException _ => "can't be found",
            DirectoryNotFoundException _ => "can't be found",
            IOException io when (io.HResult & 0xFFFF) == 32 || (io.HResult & 0xFFFF) == 33 => "in use by another program",
            InvalidDataException _ => "damaged in the zip",
            NotSupportedException _ => "packed in a way that can't be read",
            _ => e.Message,
        };

        sealed class ImportFailure : Exception
        {
            public ImportFailure(string why) : base(why) { }
        }

        /// <summary>
        /// Cleans up after an interrupted import: removes <c>*.importing</c>; a <c>*.replacing</c> goes back to its name when
        /// nothing took its place (the old picture was never replaced), else it's removed.
        /// </summary>
        static void RemoveStaleFiles(string target)
        {
            try
            {
                foreach (var file in Directory.GetFiles(target, "*" + TempSuffix))
                {
                    try { File.Delete(file); }
                    catch (Exception e) when (IsIo(e)) { }
                }
                foreach (var file in Directory.GetFiles(target, "*" + BackupSuffix))
                {
                    try
                    {
                        var original = file.Substring(0, file.Length - BackupSuffix.Length);
                        if (File.Exists(original)) File.Delete(file);
                        else File.Move(file, original);
                    }
                    catch (Exception e) when (IsIo(e)) { }
                }
            }
            catch (Exception e) when (IsIo(e)) { }
        }

        /// <summary>
        /// The target's pictures by card, as the game reads them ("89631139" for 089631139.JPG too; "back" for back.png and
        /// back.jpg when <paramref name="kind"/> is Back), each with its full paths on disk. Empty when the folder isn't there.
        /// </summary>
        static Dictionary<string, List<string>> ExistingCards(string target, PictureKind kind)
        {
            var cards = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            try
            {
                if (!Directory.Exists(target)) return cards;
                foreach (var file in Directory.EnumerateFiles(target))
                {
                    var name = Path.GetFileName(file);
                    string key;
                    if (kind == PictureKind.Back)
                    {
                        var lower = name.ToLowerInvariant();
                        if (lower != "back.png" && lower != "back.jpg") continue;
                        key = "back";
                    }
                    else if (CardPictureName(name, out var code, out _)) key = code.ToString(CultureInfo.InvariantCulture);
                    else continue;
                    if (!cards.TryGetValue(key, out var paths)) cards[key] = paths = new List<string>();
                    paths.Add(file);
                }
            }
            catch (Exception e) when (IsIo(e)) { }
            return cards;
        }

        static bool IsZip(string path) => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        /// <summary>A zip entry's file name: what follows the last '/', '\' or ':' (any of them may separate folders).</summary>
        static string FileNamePart(string fullName)
        {
            int cut = fullName.LastIndexOfAny(new[] { '/', '\\', ':' });
            return cut < 0 ? fullName : fullName.Substring(cut + 1);
        }

        /// <summary>Directories and the clutter macOS and Windows leave in zips: left out without a word.</summary>
        static bool SilentlySkipped(string fullName, string fileName)
        {
            if (fileName.Length == 0) return true;
            if (fileName.StartsWith("._", StringComparison.Ordinal)) return true;
            if (string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var part in fullName.Split('/', '\\'))
                if (string.Equals(part, "__MACOSX", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// <c>&lt;1-10 ASCII digits&gt;.&lt;jpg|jpeg|png&gt;</c>, extension in any case, the number from 1 to uint.MaxValue (the
        /// game looks pictures up by that number); gives the number and the lower-case extension.
        /// </summary>
        internal static bool CardPictureName(string name, out uint code, out string ext)
        {
            code = 0;
            ext = "";
            int dot = name.IndexOf('.');
            if (dot < 1 || dot > 10) return false;
            for (int i = 0; i < dot; i++)
                if (name[i] < '0' || name[i] > '9') return false;
            var e = name.Substring(dot + 1).ToLowerInvariant();
            if (Array.IndexOf(Extensions, e) < 0) return false;
            if (!uint.TryParse(name.Substring(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out code) || code == 0) return false;
            ext = e;
            return true;
        }

        static readonly uint[] CrcTable = MakeCrcTable();

        static uint[] MakeCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        /// <summary>Continues a CRC-32 (the zip polynomial) over <paramref name="count"/> bytes; start at 0xFFFFFFFF, invert at the end.</summary>
        static uint Crc32(uint crc, byte[] data, int count)
        {
            var table = CrcTable;
            for (int i = 0; i < count; i++) crc = table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        static PictureType ReadType(Stream s)
        {
            var header = new byte[8];
            int got = 0, n;
            while (got < header.Length && (n = s.Read(header, got, header.Length - got)) > 0) got += n;
            return TypeOf(header, got);
        }

        static PictureType TypeOf(byte[] h, int length)
        {
            if (length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47 && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A)
                return PictureType.Png;
            if (length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return PictureType.Jpeg;
            return PictureType.None;
        }

        static bool IsIo(Exception e) => e is IOException || e is UnauthorizedAccessException || e is InvalidDataException
            || e is System.Security.SecurityException || e is ArgumentException;
    }
}
