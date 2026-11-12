using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Ygo.Engine.Data;

namespace Ygo.Harness;

/// <summary>
/// `--import-test`: the card picture importer (PictureImport, docs/dev/CARD-IMPORT-SPEC-2026-11-12.md Part A). Builds tiny
/// stand-in files (PNG or JPEG magic bytes plus filler: never card pictures) and zips in a fresh temp folder, and checks
/// every rule: names and flattening, silent skips, the magic-byte check, duplicates, existing pictures with both answers,
/// the 64 MB cap (an honest header and a lying one), zip-slip names, atomic writes, one failure not stopping the rest,
/// cancel, progress, the card back and the Problem cases. Deletes the temp folder afterwards.
/// </summary>
public static class ImportTest
{
    static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    static readonly byte[] JpegMagic = { 0xFF, 0xD8, 0xFF, 0xE0 };
    const string NameHint = "Card pictures are named by card number, like 89631139.jpg.";

    public static int Run()
    {
        int failures = 0, oks = 0;
        void Check(string what, bool ok)
        {
            if (ok) oks++; else failures++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
        }

        var root = Path.Combine(Path.GetTempPath(), "dc-import-test-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "src");
        Directory.CreateDirectory(src);
        try
        {
            Body(root, src, Check);
        }
        catch (Exception e)
        {
            Check($"the test ran to the end ({e.GetType().Name}: {e.Message})", false);
        }
        finally
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(root, true);
            }
            catch (Exception e) { Console.WriteLine($"  note: couldn't delete {root}: {e.Message}"); }
        }
        Console.WriteLine($"\n{(failures == 0 ? $"All {oks} import checks passed." : $"{failures} of {oks + failures} import checks failed.")}");
        return failures == 0 ? 0 : 1;
    }

    // A stand-in picture: magic bytes, then a little text so files differ (tag) and can be told apart after a replace.
    static byte[] Png(string tag) => PngMagic.Concat(Encoding.ASCII.GetBytes("filler " + tag)).ToArray();
    static byte[] Jpeg(string tag) => JpegMagic.Concat(Encoding.ASCII.GetBytes("filler " + tag)).ToArray();

    static string Zip(string folder, string name, params (string entry, byte[]? data)[] entries)
    {
        var path = Path.Combine(folder, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, data) in entries)
        {
            var e = zip.CreateEntry(entry);
            if (data != null) using (var s = e.Open()) s.Write(data, 0, data.Length);
        }
        return path;
    }

    static string Loose(string folder, string name, byte[] data)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    static string[] Temps(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder, "*.importing") : Array.Empty<string>();
    static string[] Names(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    static bool Holds(string path, byte[] data) => File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(data);
    static bool Adds(PictureImportResult r, int total) => r.Added + r.Replaced + r.Kept + r.Failed == total;

    sealed class Sync : IProgress<(int done, int total)>
    {
        public readonly List<(int done, int total)> Seen = new();
        public Action<(int done, int total)>? Then;
        public void Report((int done, int total) value) { Seen.Add(value); Then?.Invoke(value); }
    }

    static void Body(string root, string src, Action<string, bool> Check)
    {
        // ---- 1. Names and flattening; silent skips; 2. magic bytes; 3. duplicates ----
        Console.WriteLine("Names, silent skips, magic bytes, duplicates:");
        var images = Path.Combine(root, "mods", "cards", "images");
        var zip1 = Zip(src, "pack.zip",
            ("pics/", null),                                        // a directory: silent
            ("pics/sub/10000001.jpg", Jpeg("a")),                   // flattened
            ("10000002.PNG", Png("b")),                             // upper-case extension: written lower-case
            ("deep/er/10000003.JpEg", Jpeg("c")),
            ("10000005.jpg", Png("png inside a .jpg")),             // either magic is fine
            ("other/10000001.png", Png("dup")),                     // the same card again: duplicate
            ("10000004.png", Encoding.ASCII.GetBytes("just text")), // not a picture
            ("card.png", Png("x")),                                 // not a card number
            ("12345678901.png", Png("x")),                          // 11 digits
            ("123.gif", Png("x")),
            ("123.png.txt", Png("x")),
            ("1a2.jpg", Jpeg("x")),
            ("\u0661\u0662\u0663.png", Png("x")),                   // Arabic-Indic digits are not card numbers
            ("__MACOSX/pics/._10000001.jpg", Png("mac")),           // the rest: silent
            ("__MACOSX/10000009.png", Png("mac")),
            ("pics/._10000010.png", Png("mac")),
            (".DS_Store", new byte[] { 0, 0, 0, 1 }),
            ("pics/Thumbs.db", new byte[] { 0xD0, 0xCF }));
        var plan = PictureImport.Scan(PictureKind.Standard, new[] { zip1 }, images);
        Check($"the scan has no problem ({plan.Problem ?? "none"})", plan.Problem == null);
        Check($"4 pictures to write, all new ({plan.Total}, {plan.New} new, {plan.Existing} existing)", plan.Total == 4 && plan.New == 4 && plan.Existing == 0);
        Check($"8 skipped, the directory and the macOS/Windows clutter not counted ({plan.Skipped})", plan.Skipped == 8);
        Check($"5 skipped examples, each \"name: why\" ({string.Join(" | ", plan.SkippedExamples)})",
            plan.SkippedExamples.Count == 5 && plan.SkippedExamples.All(x => x.Contains(": ")));
        Check("the duplicate is reported by its file name, \"10000001.png: duplicate\"", plan.SkippedExamples.Contains("10000001.png: duplicate"));
        Check("the text file is \"10000004.png: not a picture\"", plan.SkippedExamples.Contains("10000004.png: not a picture"));
        Check("a bad name is \"card.png: not a card number\"", plan.SkippedExamples.Contains("card.png: not a card number"));
        Check($"Kind and TargetFolder are kept (full path) ({plan.TargetFolder})", plan.Kind == PictureKind.Standard && plan.TargetFolder == Path.GetFullPath(images));
        Check("Scan writes nothing (the target isn't even made)", !Directory.Exists(images));
        var badNames = PictureImport.Scan(PictureKind.Standard, new[] { Zip(src, "names.zip",
            ("12345678901.png", Png("x")), ("123.gif", Png("x")), ("123.png.txt", Png("x")), ("1a2.jpg", Jpeg("x")),
            ("\u0661\u0662\u0663.png", Png("x")), (".png", Png("x"))) }, images);
        Check($"11 digits, .gif, .png.txt, letters, non-ASCII digits and no digits are all \"not a card number\" ({badNames.Skipped})",
            badNames.Skipped == 6 && badNames.SkippedExamples.All(x => x.EndsWith(": not a card number")));
        var maxDigits = PictureImport.Scan(PictureKind.Standard, new[] { Zip(src, "ten.zip", ("1234567890.png", Png("x")), ("7.jpg", Jpeg("x"))) }, images);
        Check("1 and 10 digits are card numbers", maxDigits.Total == 2 && maxDigits.Skipped == 0);

        var result = PictureImport.Run(plan, replaceExisting: false);
        Check($"Run adds the 4 ({result.Added} added, {result.Failed} failed)", result.Added == 4 && result.Failed == 0 && !result.Cancelled && Adds(result, 4));
        Check($"folders flattened, extensions lower-case ({string.Join(", ", Names(images))})",
            Names(images).SequenceEqual(new[] { "10000001.jpg", "10000002.png", "10000003.jpeg", "10000005.jpg" }));
        Check("the first 10000001 won (its .jpg, not the duplicate .png)", Holds(Path.Combine(images, "10000001.jpg"), Jpeg("a")));
        Check("WrittenFiles are the 4 full paths", result.WrittenFiles.Count == 4 && result.WrittenFiles.All(f => Path.IsPathRooted(f) && File.Exists(f) && Path.GetDirectoryName(f) == Path.GetFullPath(images)));
        Check("no .importing is left after a success", Temps(images).Length == 0);

        // ---- 4. Existing, across extensions, with both answers ----
        Console.WriteLine("Existing pictures:");
        var ex = Path.Combine(root, "existing");
        Directory.CreateDirectory(ex);
        File.WriteAllBytes(Path.Combine(ex, "20000001.jpg"), Jpeg("old 1"));
        File.WriteAllBytes(Path.Combine(ex, "20000002.png"), Png("old 2"));
        File.WriteAllBytes(Path.Combine(ex, "20000004.JPEG"), Jpeg("old 4")); // any case counts
        var zip2 = Zip(src, "again.zip", ("20000001.png", Png("new 1")), ("20000002.png", Png("new 2")), ("20000003.jpg", Jpeg("new 3")), ("20000004.jpg", Jpeg("new 4")));
        var plan2 = PictureImport.Scan(PictureKind.Standard, new[] { zip2 }, ex);
        Check($"3 already exist (under any extension), 1 new ({plan2.Existing} existing, {plan2.New} new)", plan2.Existing == 3 && plan2.New == 1 && plan2.Total == 4);
        var keep = PictureImport.Run(plan2, replaceExisting: false);
        Check($"Keep mine: 3 kept, 1 added ({keep.Kept} kept, {keep.Added} added, {keep.Replaced} replaced)", keep.Kept == 3 && keep.Added == 1 && keep.Replaced == 0 && Adds(keep, 4));
        Check("Keep mine leaves every old picture as it was, and adds no second extension",
            Holds(Path.Combine(ex, "20000001.jpg"), Jpeg("old 1")) && Holds(Path.Combine(ex, "20000002.png"), Png("old 2"))
            && Holds(Path.Combine(ex, "20000004.JPEG"), Jpeg("old 4")) && !File.Exists(Path.Combine(ex, "20000001.png")) && keep.RemovedFiles.Count == 0);
        var plan2b = PictureImport.Scan(PictureKind.Standard, new[] { zip2 }, ex);
        var replace = PictureImport.Run(plan2b, replaceExisting: true);
        Check($"Replace them: 4 replaced ({replace.Replaced} replaced, {replace.Added} added, {replace.Failed} failed)", replace.Replaced == 4 && replace.Added == 0 && Adds(replace, 4));
        Check($"the other extension is deleted, so each card has one picture ({string.Join(", ", Names(ex))})",
            Names(ex).SequenceEqual(new[] { "20000001.png", "20000002.png", "20000003.jpg", "20000004.jpg" }));
        Check("the new pictures are the new bytes", Holds(Path.Combine(ex, "20000001.png"), Png("new 1")) && Holds(Path.Combine(ex, "20000002.png"), Png("new 2")) && Holds(Path.Combine(ex, "20000004.jpg"), Jpeg("new 4")));
        Check($"RemovedFiles names the deleted .jpg and .JPEG ({string.Join(", ", replace.RemovedFiles.Select(Path.GetFileName))})",
            replace.RemovedFiles.Count == 2 && replace.RemovedFiles.Any(f => f.EndsWith("20000001.jpg")) && replace.RemovedFiles.Any(f => f.EndsWith("20000004.JPEG")) && replace.RemovedFiles.All(Path.IsPathRooted));
        Check("WrittenFiles has all 4", replace.WrittenFiles.Count == 4);

        // ---- 5. The 64 MB cap ----
        Console.WriteLine("The 64 MB cap:");
        var cap = Path.Combine(root, "cap");
        var bigZip = Path.Combine(src, "big.zip");
        using (var zip = ZipFile.Open(bigZip, ZipArchiveMode.Create))
        {
            foreach (var (name, size) in new[] { ("30000001.png", PictureImport.MaxPictureBytes + 1), ("30000002.png", PictureImport.MaxPictureBytes + 1), ("30000003.png", 1000L), ("30000004.png", 1000L) })
            {
                using var s = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                s.Write(PngMagic, 0, PngMagic.Length);
                var zeros = new byte[1 << 20];
                for (long left = size - PngMagic.Length; left > 0; left -= zeros.Length) s.Write(zeros, 0, (int)Math.Min(left, zeros.Length));
            }
            using (var ok = zip.CreateEntry("30000005.png").Open()) ok.Write(Png("fine"));
        }
        // 30000002 lies: its headers say 100 bytes. 30000003 says 2000 and 30000004 says 500 (a damaged zip).
        PatchSize(bigZip, "30000002.png", 100);
        PatchSize(bigZip, "30000003.png", 2000);
        PatchSize(bigZip, "30000004.png", 500);
        Console.WriteLine($"  (big.zip is {new FileInfo(bigZip).Length / 1024} KB)");
        var clock = Stopwatch.StartNew();
        var plan3 = PictureImport.Scan(PictureKind.Standard, new[] { bigZip }, cap);
        var scanMs = clock.ElapsedMilliseconds;
        Check("an entry declaring over 64 MB is skipped as \"too big\" at the scan", plan3.SkippedExamples.Contains("30000001.png: too big") && plan3.Skipped == 1);
        Check($"Scan doesn't decompress whole entries: the lying 64 MB entry and the mis-sized ones pass on their headers ({plan3.Total} to write, {scanMs} ms)", plan3.Total == 4);
        var r3 = PictureImport.Run(plan3, replaceExisting: true);
        // A runtime that trusts the header stops at 100 bytes (the checksum catches it); one that doesn't hits the cap.
        Check($"the lying entry is never written: \"too big\" or \"damaged in the zip\" ({string.Join(" | ", r3.FailedExamples)})",
            r3.FailedExamples.Contains("30000002.png: too big") || r3.FailedExamples.Contains("30000002.png: damaged in the zip"));
        Check("an entry shorter or longer than its header says fails (damaged), the rest goes in",
            r3.Failed == 3 && r3.Added == 1 && Names(cap).SequenceEqual(new[] { "30000005.png" }));
        Check("nothing over the cap (or half-written) is on disk, no .importing", Temps(cap).Length == 0 && Directory.GetFiles(cap).All(f => new FileInfo(f).Length <= PictureImport.MaxPictureBytes));

        // ---- Zip-slip names ----
        Console.WriteLine("Zip-slip names:");
        var deepTarget = Path.Combine(root, "a", "b", "c", "images");
        var slip = Zip(src, "slip.zip",
            ("../../evil.png", Png("evil")),
            ("../../../../40000001.png", Png("1")),
            ("/abs/40000002.png", Png("2")),
            ("C:\\abs\\40000003.png", Png("3")),
            ("..\\..\\40000004.png", Png("4")),
            ("C:40000005.png", Png("5")));
        var plan4 = PictureImport.Scan(PictureKind.Standard, new[] { slip }, deepTarget);
        var r4 = PictureImport.Run(plan4, replaceExisting: true);
        Check($"\"../../evil.png\" is skipped (not a card number) ({string.Join(" | ", plan4.SkippedExamples)})", plan4.SkippedExamples.Contains("evil.png: not a card number"));
        Check($"../, absolute, drive and backslash names land flat in the target ({string.Join(", ", Names(deepTarget))})",
            r4.Added == 5 && Names(deepTarget).SequenceEqual(new[] { "40000001.png", "40000002.png", "40000003.png", "40000004.png", "40000005.png" }));
        var outside = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is "evil.png" or "40000001.png" or "40000002.png" or "40000003.png" or "40000004.png" or "40000005.png")
            .Where(f => Path.GetDirectoryName(f) != Path.GetFullPath(deepTarget)).ToList();
        var parent = Path.GetDirectoryName(root)!;
        Check("nothing is written anywhere else (the temp folder, its parent, the drive root)",
            outside.Count == 0 && !File.Exists(Path.Combine(parent, "evil.png")) && !File.Exists(Path.Combine(parent, "40000001.png"))
            && !File.Exists(Path.Combine(Path.GetPathRoot(root)!, "abs", "40000002.png")) && r4.WrittenFiles.All(f => Path.GetDirectoryName(f) == Path.GetFullPath(deepTarget)));

        // ---- 6. Atomic writes; stale .importing ----
        Console.WriteLine("Atomic writes:");
        var at = Path.Combine(root, "atomic");
        Directory.CreateDirectory(at);
        File.WriteAllBytes(Path.Combine(at, "50000001.png.importing"), Png("half"));
        File.WriteAllBytes(Path.Combine(at, "99999999.jpg.importing"), Png("half"));
        var plan5 = PictureImport.Scan(PictureKind.Standard, new[] { Loose(src, "50000001.png", Png("whole")) }, at);
        Check("Scan ignores a stale .importing (the card counts as new)", plan5.New == 1 && plan5.Existing == 0);
        var r5 = PictureImport.Run(plan5, replaceExisting: false);
        Check("Run removes every stale .importing and writes the picture", r5.Added == 1 && Temps(at).Length == 0 && Holds(Path.Combine(at, "50000001.png"), Png("whole")));

        // ---- 7. One failure doesn't stop the rest ----
        Console.WriteLine("Failures:");
        var fl = Path.Combine(root, "fail");
        Directory.CreateDirectory(fl);
        if (OperatingSystem.IsWindows())
        {
            var locked = Path.Combine(fl, "60000002.png");
            File.WriteAllBytes(locked, Png("old"));
            File.SetAttributes(locked, FileAttributes.ReadOnly);
            var plan6 = PictureImport.Scan(PictureKind.Standard, new[] { Zip(src, "fail.zip", ("60000001.png", Png("1")), ("60000002.png", Png("2")), ("60000003.png", Png("3"))) }, fl);
            var r6 = PictureImport.Run(plan6, replaceExisting: true);
            Check($"a read-only picture fails, the others go in ({r6.Added} added, {r6.Failed} failed: {string.Join(" | ", r6.FailedExamples)})",
                r6.Added == 2 && r6.Failed == 1 && Adds(r6, 3) && File.Exists(Path.Combine(fl, "60000003.png")));
            Check("the failure's reason is plain (\"60000002.png: the old picture can't be replaced\")", r6.FailedExamples.SequenceEqual(new[] { "60000002.png: the old picture can't be replaced" }));
            Check("the read-only picture is untouched, no .importing left after a failure", Holds(locked, Png("old")) && Temps(fl).Length == 0 && !r6.WrittenFiles.Contains(locked));
            File.SetAttributes(locked, FileAttributes.Normal);
        }
        else Console.WriteLine("  note: the read-only failure is checked on Windows only");
        var gone = Loose(src, "60000009.png", Png("soon gone"));
        var plan6b = PictureImport.Scan(PictureKind.Standard, new[] { gone, Loose(src, "60000010.png", Png("stays")) }, fl);
        File.Delete(gone);
        var r6b = PictureImport.Run(plan6b, replaceExisting: true);
        Check($"a file gone between Scan and Run fails alone ({string.Join(" | ", r6b.FailedExamples)})", r6b.FailedExamples.SequenceEqual(new[] { "60000009.png: can't be found" }) && r6b.Failed == 1 && r6b.Added == 1 && Temps(fl).Length == 0);

        // ---- Cancel and progress ----
        Console.WriteLine("Cancel and progress:");
        var many = Enumerable.Range(0, 40).Select(i => ($"pics/{70000000 + i}.png", (byte[]?)Png(i.ToString()))).ToArray();
        var manyZip = Zip(src, "many.zip", many);
        var cn = Path.Combine(root, "cancel");
        using (var cts = new CancellationTokenSource())
        {
            var p = new Sync { Then = v => { if (v.done >= 1) cts.Cancel(); } };
            var r7 = PictureImport.Run(PictureImport.Scan(PictureKind.Standard, new[] { manyZip }, cn), true, p, cts.Token);
            Check($"Cancel stops between pictures: 1 written, Cancelled ({r7.Added} added)", r7.Cancelled && r7.Added == 1 && Names(cn).Length == 1);
            Check("no .importing after a cancel", Temps(cn).Length == 0);
        }
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            var cn2 = Path.Combine(root, "cancel2");
            var r7b = PictureImport.Run(PictureImport.Scan(PictureKind.Standard, new[] { manyZip }, cn2), true, null, cts.Token);
            Check("cancelled before it starts: nothing written", r7b.Cancelled && r7b.Added == 0 && Names(cn2).Length == 0);
        }
        var pr = new Sync();
        var prDir = Path.Combine(root, "progress");
        clock.Restart();
        var r8 = PictureImport.Run(PictureImport.Scan(PictureKind.Standard, new[] { manyZip }, prDir), true, pr);
        var ms = clock.ElapsedMilliseconds;
        Check($"progress starts at (0, 40) and ends at (40, 40) ({pr.Seen.Count} reports in {ms} ms)",
            r8.Added == 40 && pr.Seen.Count >= 2 && pr.Seen[0] == (0, 40) && pr.Seen[^1] == (40, 40));
        Check("progress only goes up and is throttled (at most the start, the end and one per 50 ms)",
            pr.Seen.Zip(pr.Seen.Skip(1)).All(x => x.Second.done >= x.First.done) && pr.Seen.Count <= 3 + ms / 50);

        // ---- 8. The card back ----
        Console.WriteLine("Card back:");
        var cards = Path.Combine(root, "mods", "cards");
        var backZip = PictureImport.Scan(PictureKind.Back, new[] { zip1 }, cards);
        Check($"a zip is a Problem (\"{backZip.Problem}\")", backZip.Problem == "Pick one picture for the card back." && backZip.Total == 0);
        var two = PictureImport.Scan(PictureKind.Back, new[] { Loose(src, "b1.png", Png("1")), Loose(src, "b2.png", Png("2")) }, cards);
        Check("two files are a Problem", two.Problem == "Pick one picture for the card back.");
        var notPic = PictureImport.Scan(PictureKind.Back, new[] { Loose(src, "notes.png", Encoding.ASCII.GetBytes("text")) }, cards);
        Check($"not a picture is a Problem (\"{notPic.Problem}\")", notPic.Problem == "notes.png isn't a picture the game can read. Use a .png or .jpg." && notPic.Total == 0);
        var backMissing = PictureImport.Scan(PictureKind.Back, new[] { Path.Combine(src, "nope.png") }, cards);
        Check("a missing back is a Problem", backMissing.Problem == "nope.png can't be found.");
        var b1 = PictureImport.Scan(PictureKind.Back, new[] { Loose(src, "My Back.JPG", Png("back png")) }, cards);
        Check("any name will do, no existing back yet", b1.Problem == null && b1.Total == 1 && b1.New == 1 && b1.Existing == 0);
        var rb1 = PictureImport.Run(b1, false);
        Check("written as back.png by its real type (PNG bytes in a .JPG)", rb1.Added == 1 && Holds(Path.Combine(cards, "back.png"), Png("back png")) && !File.Exists(Path.Combine(cards, "back.jpg")));
        var b2 = PictureImport.Scan(PictureKind.Back, new[] { Loose(src, "other.png", Jpeg("back jpg")) }, cards);
        Check("back.png there: Existing is 1", b2.Existing == 1 && b2.New == 0);
        var rb2 = PictureImport.Run(b2, false);
        Check("Keep mine keeps back.png and writes no back.jpg", rb2.Kept == 1 && Holds(Path.Combine(cards, "back.png"), Png("back png")) && !File.Exists(Path.Combine(cards, "back.jpg")));
        var rb3 = PictureImport.Run(PictureImport.Scan(PictureKind.Back, new[] { Path.Combine(src, "other.png") }, cards), true);
        Check("Replace writes back.jpg (JPEG bytes) and deletes back.png",
            rb3.Replaced == 1 && Holds(Path.Combine(cards, "back.jpg"), Jpeg("back jpg")) && !File.Exists(Path.Combine(cards, "back.png"))
            && rb3.RemovedFiles.SequenceEqual(new[] { Path.Combine(Path.GetFullPath(cards), "back.png") }) && rb3.WrittenFiles.SequenceEqual(new[] { Path.Combine(Path.GetFullPath(cards), "back.jpg") }));
        Check("the back writes nothing but back.*, no .importing", Names(cards).SequenceEqual(new[] { "back.jpg" }));

        // ---- Review fixes (2026-11-12): replace order, card numbers, a stuck old extension, disk space, example names ----
        Console.WriteLine("Replacing safely:");
        var rs = Path.Combine(root, "replace-safe");
        Directory.CreateDirectory(rs);
        File.WriteAllBytes(Path.Combine(rs, "90000001.png"), Png("old 1"));
        File.WriteAllBytes(Path.Combine(rs, "90000002.jpg"), Jpeg("old 2"));
        var planRs = PictureImport.Scan(PictureKind.Standard, new[] { Zip(src, "rs.zip", ("90000001.png", Png("new 1")), ("90000002.png", Png("new 2"))) }, rs);
        PictureImport.Hooks.Move = (from, to) =>
        {
            if (from.EndsWith(".importing")) throw new IOException("held by another program");
            File.Move(from, to);
        };
        PictureImportResult rRs;
        try { rRs = PictureImport.Run(planRs, replaceExisting: true); }
        finally { PictureImport.Hooks.Move = null; }
        Check($"the new picture can't move in: both fail ({rRs.Failed} failed: {string.Join(" | ", rRs.FailedExamples)})", rRs.Failed == 2 && rRs.WrittenFiles.Count == 0 && rRs.RemovedFiles.Count == 0);
        Check($"the old pictures are still there under their names ({string.Join(", ", Names(rs))})",
            Holds(Path.Combine(rs, "90000001.png"), Png("old 1")) && Holds(Path.Combine(rs, "90000002.jpg"), Jpeg("old 2")) && Names(rs).Length == 2);
        File.WriteAllBytes(Path.Combine(rs, "90000003.png.replacing"), Png("orphan"));   // an old picture a crash left renamed
        File.WriteAllBytes(Path.Combine(rs, "90000001.png.replacing"), Png("leftover")); // a backup whose replacement is in
        var rRs2 = PictureImport.Run(PictureImport.Scan(PictureKind.Standard, new[] { Loose(src, "90000004.png", Png("4")) }, rs), true);
        Check($"Run puts an orphaned .replacing back and removes a leftover one ({string.Join(", ", Names(rs))})",
            rRs2.Added == 1 && Holds(Path.Combine(rs, "90000003.png"), Png("orphan")) && Holds(Path.Combine(rs, "90000001.png"), Png("old 1"))
            && Directory.GetFiles(rs, "*.replacing").Length == 0);
        var rRs3 = PictureImport.Run(PictureImport.Scan(PictureKind.Standard, new[] { Path.Combine(src, "rs.zip") }, rs), true);
        Check("a normal replace leaves no .replacing or .importing", rRs3.Replaced == 2 && Directory.GetFiles(rs, "*.replacing").Length == 0 && Temps(rs).Length == 0
            && Holds(Path.Combine(rs, "90000002.png"), Png("new 2")) && !File.Exists(Path.Combine(rs, "90000002.jpg")));

        Console.WriteLine("Card numbers as the game reads them:");
        var nums = Path.Combine(root, "numbers");
        Directory.CreateDirectory(nums);
        File.WriteAllBytes(Path.Combine(nums, "007.png"), Png("old 7"));
        var planN = PictureImport.Scan(PictureKind.Standard, new[] { Zip(src, "nums.zip",
            ("0012345.jpg", Jpeg("a")), ("4294967296.png", Png("x")), ("4294967295.png", Png("max")), ("0.png", Png("x")), ("000.png", Png("x")),
            ("01.jpg", Jpeg("one")), ("1.png", Png("one again")), ("7.jpg", Jpeg("new 7"))) }, nums);
        Check($"over uint, zero and 000 are \"not a card number\"; 1.png after 01.jpg is a duplicate ({string.Join(" | ", planN.SkippedExamples)})",
            planN.Skipped == 4 && planN.SkippedExamples.SequenceEqual(new[] { "4294967296.png: not a card number", "0.png: not a card number", "000.png: not a card number", "1.png: duplicate" }));
        Check($"007.png on disk makes 7.jpg existing ({planN.Existing} existing of {planN.Total})", planN.Total == 4 && planN.Existing == 1);
        var rN = PictureImport.Run(planN, true);
        Check($"written without leading zeros, 007.png replaced by 7.jpg ({string.Join(", ", Names(nums))})",
            rN.Added == 3 && rN.Replaced == 1 && Names(nums).SequenceEqual(new[] { "1.jpg", "12345.jpg", "4294967295.png", "7.jpg" })
            && rN.RemovedFiles.Count == 1 && rN.RemovedFiles[0].EndsWith("007.png"));

        Console.WriteLine("An old picture that can't be removed:");
        var stuck = Path.Combine(root, "stuck");
        Directory.CreateDirectory(stuck);
        var oldJpg = Path.Combine(stuck, "91000001.jpg");
        File.WriteAllBytes(oldJpg, Jpeg("old"));
        File.SetAttributes(oldJpg, FileAttributes.ReadOnly);
        var rSt = PictureImport.Run(PictureImport.Scan(PictureKind.Standard, new[] { Loose(src, "91000001.png", Png("new")), Loose(src, "91000002.png", Png("other")) }, stuck), true);
        File.SetAttributes(oldJpg, FileAttributes.Normal);
        Check($"it fails as \"the old picture can't be replaced\", the next one goes in ({string.Join(" | ", rSt.FailedExamples)})",
            rSt.Failed == 1 && rSt.Added == 1 && rSt.FailedExamples.SequenceEqual(new[] { "91000001.png: the old picture can't be replaced" }));
        Check($"the old .jpg stays alone, the new .png isn't left beside it or listed ({string.Join(", ", Names(stuck))})",
            Holds(oldJpg, Jpeg("old")) && !File.Exists(Path.Combine(stuck, "91000001.png")) && !rSt.WrittenFiles.Any(f => f.EndsWith("91000001.png"))
            && Directory.GetFiles(stuck, "*.replacing").Length == 0 && Temps(stuck).Length == 0);

        Console.WriteLine("Disk space:");
        var ds = Path.Combine(root, "space");
        var small = Zip(src, "small.zip", ("93000001.png", Png("1")), ("93000002.png", Png("2")));
        var gbZip = Zip(src, "gb.zip", Enumerable.Range(0, 20).Select(i => ($"{94000000 + i}.png", (byte[]?)Png(i.ToString()))).ToArray());
        for (int i = 0; i < 20; i++) PatchSize(gbZip, $"{94000000 + i}.png", 60u * 1024 * 1024);
        try
        {
            PictureImport.Hooks.FreeSpace = _ => 10;
            var noRoom = PictureImport.Scan(PictureKind.Standard, new[] { small }, ds);
            Check($"not enough room is a Problem (\"{noRoom.Problem}\")", noRoom.Problem == "There isn't enough room on the disk for these pictures. They need about 1 MB." && noRoom.Total == 0);
            var noRoomGb = PictureImport.Scan(PictureKind.Standard, new[] { gbZip }, ds);
            Check($"from 1024 MB it says GB (\"{noRoomGb.Problem}\")", noRoomGb.Problem == "There isn't enough room on the disk for these pictures. They need about 1.2 GB.");
            Check("the card back is checked too", PictureImport.Scan(PictureKind.Back, new[] { Loose(src, "bk.png", Png("bk")) }, ds).Problem?.StartsWith("There isn't enough room") == true);
            PictureImport.Hooks.FreeSpace = _ => null;
            Check("free space that can't be read: carry on", PictureImport.Scan(PictureKind.Standard, new[] { small }, ds).Problem == null);
            PictureImport.Hooks.FreeSpace = _ => 1L << 40;
            Check("enough room: no Problem", PictureImport.Scan(PictureKind.Standard, new[] { gbZip }, ds).Problem == null);
        }
        finally { PictureImport.Hooks.FreeSpace = null; }
        var fullPlan = PictureImport.Scan(PictureKind.Standard, new[] { Zip(src, "full.zip", Enumerable.Range(0, 5).Select(i => ($"{95000000 + i}.png", (byte[]?)Png(i.ToString()))).ToArray()) }, ds);
        int moves = 0;
        var fullProgress = new Sync();
        PictureImport.Hooks.Move = (from, to) =>
        {
            if (from.EndsWith(".importing") && ++moves >= 2) throw new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
            File.Move(from, to);
        };
        PictureImportResult rFull;
        try { rFull = PictureImport.Run(fullPlan, true, fullProgress); }
        finally { PictureImport.Hooks.Move = null; }
        Check($"a full disk stops the import: 1 added, the other 4 \"the disk is full\" after one try ({rFull.Added} added, {rFull.Failed} failed, {moves} moves tried)",
            rFull.Added == 1 && rFull.Failed == 4 && moves == 2 && rFull.FailedExamples.All(x => x.EndsWith(": the disk is full")) && Names(ds).Length == 1 && Temps(ds).Length == 0);
        Check("its progress still ends at (5, 5)", fullProgress.Seen.Count > 0 && fullProgress.Seen[^1] == (5, 5));

        Console.WriteLine("Example names:");
        var exNames = PictureImport.Scan(PictureKind.Standard, new[] { Zip(src, "exnames.zip", ("pics/notes.txt", Encoding.ASCII.GetBytes("hi")), ("deep/er/96000001.png", Png("ok"))) }, ds);
        Check($"examples show the file name only (\"{string.Join(" | ", exNames.SkippedExamples)}\")", exNames.SkippedExamples.SequenceEqual(new[] { "notes.txt: not a card number" }));

        // ---- Problems; a mix of a zip and loose files ----
        Console.WriteLine("Problems and mixes:");
        var junk = Loose(src, "broken.zip", Encoding.ASCII.GetBytes("this is not a zip at all, not even close"));
        var p1 = PictureImport.Scan(PictureKind.Standard, new[] { junk }, images);
        Check($"an unreadable zip (\"{p1.Problem}\")", p1.Problem == "broken.zip can't be read. It may be damaged, or not fully downloaded." && p1.Total == 0);
        var noPics = Zip(src, "nothing.zip", ("readme.txt", Encoding.ASCII.GetBytes("hi")), ("__MACOSX/._x", Png("x")));
        var p2 = PictureImport.Scan(PictureKind.Standard, new[] { noPics }, images);
        Check($"nothing importable in one zip (\"{p2.Problem}\")", p2.Problem == $"There are no card pictures in nothing.zip. {NameHint}" && p2.Skipped == 1);
        var p2b = PictureImport.Scan(PictureKind.Standard, new[] { Loose(src, "a.png", Png("a")), Loose(src, "b.jpg", Jpeg("b")) }, images);
        Check("nothing importable in several loose files", p2b.Problem == $"None of these files is a card picture. {NameHint}");
        var p3 = PictureImport.Scan(PictureKind.Standard, new[] { zip1, Path.Combine(src, "missing.zip") }, images);
        Check($"a missing file (\"{p3.Problem}\")", p3.Problem == "missing.zip can't be found.");
        var p4 = PictureImport.Scan(PictureKind.Standard, Array.Empty<string>(), images);
        Check("no files at all is a Problem", p4.Problem != null);
        var rp = PictureImport.Run(p1, true);
        Check("Run on a plan with a Problem does nothing", rp.Added + rp.Failed + rp.Kept + rp.Replaced == 0 && rp.WrittenFiles.Count == 0);
        var mixDir = Path.Combine(root, "mix");
        var mix = PictureImport.Scan(PictureKind.Hd, new[] { zip2, Loose(src, "80000001.PNG", Png("loose")), Loose(src, "photo.png", Png("x")), Loose(src, "20000001.jpg", Jpeg("dup of the zip's")) }, mixDir);
        Check($"a zip plus loose files: zip entries and loose pictures together, loose duplicates and names skipped ({mix.Total} total, {mix.Skipped} skipped)",
            mix.Problem == null && mix.Total == 5 && mix.Skipped == 2 && mix.SkippedExamples.Contains("photo.png: not a card number") && mix.SkippedExamples.Contains("20000001.jpg: duplicate"));
        var rmix = PictureImport.Run(mix, false);
        Check("HD pictures go to their own folder", rmix.Added == 5 && File.Exists(Path.Combine(mixDir, "80000001.png")) && mix.Kind == PictureKind.Hd);
    }

    /// <summary>Rewrites an entry's uncompressed size in both its local header and the central directory (a lying or damaged zip).</summary>
    static void PatchSize(string zipPath, string entryName, uint size)
    {
        var bytes = File.ReadAllBytes(zipPath);
        var name = Encoding.UTF8.GetBytes(entryName);
        int patched = 0;
        for (int i = 0; i + 46 < bytes.Length; i++)
        {
            if (bytes[i] != 0x50 || bytes[i + 1] != 0x4B) continue;
            int sig = BitConverter.ToInt32(bytes, i);
            int nameAt, sizeAt, nameLen;
            if (sig == 0x04034b50) { nameLen = BitConverter.ToUInt16(bytes, i + 26); nameAt = i + 30; sizeAt = i + 22; }
            else if (sig == 0x02014b50) { nameLen = BitConverter.ToUInt16(bytes, i + 28); nameAt = i + 46; sizeAt = i + 24; }
            else continue;
            if (nameLen != name.Length || nameAt + nameLen > bytes.Length || !bytes.AsSpan(nameAt, nameLen).SequenceEqual(name)) continue;
            BitConverter.GetBytes(size).CopyTo(bytes, sizeAt);
            patched++;
        }
        if (patched != 2) throw new InvalidOperationException($"patched {patched} headers of {entryName}, not 2");
        File.WriteAllBytes(zipPath, bytes);
    }
}
