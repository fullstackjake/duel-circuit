using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;

namespace YgoCampaign.Game
{
    /// <summary>
    /// Turns a card picture file into a texture (see <see cref="CardArt"/> for where the files come from).
    /// <para>
    /// Everyday pictures (<see cref="Load"/>) are capped at <see cref="Cap"/> pixels tall and get mipmaps, so a card stays
    /// sharp from the board to the detail panel on a Retina screen without aliasing when small. Big pictures (HD ones,
    /// from 400 pixels tall) are also compressed (DXT1, a sixth of the memory) and kept in a disk cache of those
    /// compressed bytes, so each file is decoded only once ever: an 813x1185 JPEG costs tens of milliseconds to decode,
    /// shrink and compress, a cached one well under one. Small pictures (EDOPro's 177x254) stay uncompressed at their size.
    /// </para>
    /// <para>
    /// <see cref="LoadFull"/> keeps the whole picture (mipmapped, uncompressed) for the few big views where it shows: the
    /// card readers and the full-size slab. It's cached on disk too (about 3.8 MB a picture, uncompressed so text stays
    /// clean), under its own smaller budget, so reopening a reader never decodes again.
    /// </para>
    /// <para>
    /// The cache is safe to share and to lose: each file is written under its own temporary name and moved into place
    /// whole, a file that doesn't read back exactly is deleted and made again, and the folder is trimmed when the game
    /// starts and again every <see cref="TrimEvery"/> bytes written.
    /// </para>
    /// </summary>
    public static class ArtLoader
    {
        /// <summary>
        /// The tallest everyday picture. The biggest everyday views (the detail panel's picture, the hand, spotlights) are
        /// up to about 380 units tall in the 1920x1080 layout, which a Retina screen draws at about 2 pixels per unit.
        /// </summary>
        public const int Cap = 768;

        /// <summary>
        /// How much each mipmap level below the full one is sharpened. Pictures are filtered trilinearly (a card drawn at,
        /// say, 1.6 levels down blends levels one and two, so it never pops or shimmers as it moves), which on its own
        /// looks soft; sharpened levels give the detail back. (A negative mip bias would do it too, but Metal ignores
        /// Texture.mipMapBias.)
        /// </summary>
        private const float MipSharpen = 0.8f;

        /// <summary>Pictures this tall or taller are compressed (and cached on disk); smaller ones aren't worth it.</summary>
        public const int CompressFrom = 400;

        /// <summary>Bumped when the cached pictures' format or the way they're made changes (old files are remade).</summary>
        private const int CacheVersion = 2;
        private const uint CacheMagic = 0x54524159; // "YART"
        /// <summary>The disk cache is trimmed (least recently used first) back under these many bytes: everyday and whole pictures.</summary>
        private const long CacheBudget = 1L << 30, FullBudget = 256L << 20;
        /// <summary>A long session trims the cache again after writing this much, so it never grows far past its budget.</summary>
        private const long TrimEvery = 128L << 20;
        private const string Everyday = ".art", Whole = ".full";
        private static long _written;
        private static int _trimming;

        /// <summary>Where the compressed pictures are kept (set by <see cref="CardArt"/>); null keeps none.</summary>
        public static string? CacheFolder { get; set; }

        /// <summary>For checks: pictures decoded from their files, read back from the disk cache, and the time spent on both.</summary>
        public static int Decoded, CacheHits;
        public static double DecodeMs, CacheMs;
        /// <summary>For checks: time spent decoding files, shrinking, making mipmaps and compressing (first-time loads).</summary>
        public static readonly double[] StageMs = new double[4];
        /// <summary>For checks: the slower, better DXT compressor (the default) or the quick one.</summary>
        internal static bool HighQuality = true;
        private static readonly System.Diagnostics.Stopwatch Stage = new System.Diagnostics.Stopwatch();
        private static void Lap(int stage) { StageMs[stage] += Stage.Elapsed.TotalMilliseconds; Stage.Restart(); }

        // ---- Picture size from the file's header -------------------------------------------------------

        /// <summary>A JPEG's or PNG's size in pixels, read from its header without decoding it; (0, 0) when it can't tell.</summary>
        public static (int width, int height) ImageSize(string path)
        {
            try
            {
                using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
                var head = new byte[24];
                if (f.Read(head, 0, 24) < 24) return (0, 0);
                if (head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G')
                    return (BigEndian(head, 16, 4), BigEndian(head, 20, 4)); // IHDR comes first
                if (head[0] != 0xFF || head[1] != 0xD8) return (0, 0);
                // JPEG: walk the segments to the frame header (SOF0-SOF15, except DHT, JPG and DAC).
                f.Position = 2;
                var seg = new byte[9];
                while (f.Read(seg, 0, 4) == 4)
                {
                    if (seg[0] != 0xFF) return (0, 0);
                    int marker = seg[1];
                    if (marker == 0xFF) { f.Position -= 3; continue; } // fill byte
                    int length = BigEndian(seg, 2, 2);
                    if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                    {
                        if (f.Read(seg, 0, 5) < 5) return (0, 0);
                        return (BigEndian(seg, 3, 2), BigEndian(seg, 1, 2));
                    }
                    if (length < 2) return (0, 0);
                    f.Position += length - 2;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return (0, 0);
        }

        private static int BigEndian(byte[] b, int at, int count)
        {
            int v = 0;
            for (int i = 0; i < count; i++) v = (v << 8) | b[at + i];
            return v;
        }

        // ---- Everyday pictures -------------------------------------------------------------------------

        /// <summary>
        /// The everyday texture for a picture file: at most <see cref="Cap"/> tall, mipmapped, compressed when big (from the
        /// disk cache when it's there). Pictures that aren't card-shaped are letterboxed. Null when the file can't be read.
        /// </summary>
        public static Texture2D? Load(string path, string name)
        {
            var cached = ReadCache(path, name, Everyday);
            if (cached != null) return MarkBoxed(cached, path);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var source = Decode(path);
            if (source == null) return null;
            Texture2D texture;
            if (source.height < CompressFrom)
            {
                // Small: as it was, plus mipmaps.
                texture = WithMips(source, source.width, source.height, name, compress: false);
            }
            else
            {
                int h = Math.Min(Cap, source.height) & ~3;
                int w = source.height <= Cap ? source.width & ~3 : Math.Max(4, (int)Math.Round(source.width * (double)h / source.height / 4) * 4);
                texture = WithMips(source, w, h, name, compress: SystemInfo.SupportsTextureFormat(TextureFormat.DXT1));
                if (texture.format == TextureFormat.DXT1 || texture.format == TextureFormat.DXT5) WriteCache(path, texture, Everyday);
                if (texture.isReadable) texture.Apply(false, makeNoLongerReadable: true);
            }
            UnityEngine.Object.Destroy(source);
            Decoded++;
            DecodeMs += watch.Elapsed.TotalMilliseconds;
            return MarkBoxed(texture, path);
        }

        /// <summary>The end of a letterboxed picture's texture name (the mark goes with the texture, so nothing is kept for it).</summary>
        private const string BoxedTag = " (letterboxed)";

        /// <summary>
        /// Whether a card picture was letterboxed (a manga panel, a wide scan): the card's layout (name bar, stars, text box)
        /// isn't where the foil would look for it (see CardFoil).
        /// </summary>
        public static bool IsLetterboxed(Texture? texture) => texture != null && texture.name.EndsWith(BoxedTag, StringComparison.Ordinal);

        /// <summary>A picture texture's name without the letterbox mark ("art 89631139 manga").</summary>
        public static string PlainName(Texture texture) =>
            IsLetterboxed(texture) ? texture.name.Substring(0, texture.name.Length - BoxedTag.Length) : texture.name;

        /// <summary>Marks the texture as letterboxed when its file isn't card-shaped (the same test <see cref="Decode"/> uses).</summary>
        private static Texture2D MarkBoxed(Texture2D texture, string path)
        {
            var (w, h) = ImageSize(path);
            if (w > 0 && h > 0 && Mathf.Abs((float)w / h / CardArt.CardAspect - 1) > 0.06f && !IsLetterboxed(texture)) texture.name += BoxedTag;
            return texture;
        }

        /// <summary>The whole picture, mipmapped and uncompressed (letterboxed when it isn't card-shaped), for the big views.</summary>
        public static Texture2D? LoadFull(string path, string name)
        {
            var cached = ReadCache(path, name + " full", Whole);
            if (cached != null) return MarkBoxed(cached, path);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var source = Decode(path);
            if (source == null) return null;
            var texture = WithMips(source, source.width, source.height, name + " full", compress: false, keepReadable: true);
            UnityEngine.Object.Destroy(source);
            WriteCache(path, texture, Whole);
            texture.Apply(false, makeNoLongerReadable: true);
            Decoded++;
            DecodeMs += watch.Elapsed.TotalMilliseconds;
            return MarkBoxed(texture, path);
        }

        /// <summary>The picture as the game loaded it before HD pictures (full size, no mipmaps), for checks to compare against.</summary>
        public static Texture2D? LoadPlain(string path, string name)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = name };
            if (texture.LoadImage(File.ReadAllBytes(path), markNonReadable: true)) return texture;
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        /// <summary>Decodes a file (readable, no mipmaps), letterboxed onto a card-shaped picture when it isn't card-shaped.</summary>
        private static Texture2D? Decode(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
            Stage.Restart();
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!texture.LoadImage(bytes, markNonReadable: false))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            Lap(0);
            float aspect = (float)texture.width / texture.height;
            if (Mathf.Abs(aspect / CardArt.CardAspect - 1) <= 0.06f) return texture;
            // Not card-shaped (a manga panel, a wide scan): letterboxed onto a card-shaped picture so it isn't stretched.
            int w = texture.width, h = texture.height;
            int outW = aspect > CardArt.CardAspect ? w : Mathf.RoundToInt(h * CardArt.CardAspect);
            int outH = aspect > CardArt.CardAspect ? Mathf.RoundToInt(w / CardArt.CardAspect) : h;
            var boxed = new Texture2D(outW, outH, TextureFormat.RGB24, false);
            var fill = new Color32[outW * outH];
            var edge = new Color32(14, 15, 20, 255);
            for (int i = 0; i < fill.Length; i++) fill[i] = edge;
            boxed.SetPixels32(fill);
            boxed.SetPixels32((outW - w) / 2, (outH - h) / 2, w, h, texture.GetPixels32());
            boxed.Apply(false);
            UnityEngine.Object.Destroy(texture);
            return boxed;
        }

        /// <summary>
        /// A new texture of <paramref name="source"/>'s pixels at <paramref name="w"/>x<paramref name="h"/> (shrunk with an
        /// area average, or trimmed by up to 3 pixels to a multiple of 4), with mipmaps. Compressed if asked, and then still
        /// readable (for the disk cache; the caller makes it unreadable); otherwise unreadable already, unless
        /// <paramref name="keepReadable"/> (for the disk cache).
        /// </summary>
        private static Texture2D WithMips(Texture2D source, int w, int h, string name, bool compress, bool keepReadable = false)
        {
            var format = source.format == TextureFormat.RGB24 ? TextureFormat.RGB24 : TextureFormat.RGBA32;
            int channels = format == TextureFormat.RGB24 ? 3 : 4;
            var texture = new Texture2D(w, h, format, true) { name = name, filterMode = FilterMode.Trilinear };
            var from = source.format == format ? source.GetPixelData<byte>(0) : default;
            byte[]? converted = null;
            if (!from.IsCreated)
            {
                // A PNG decoded as some other format: go through Color32.
                var px = source.GetPixels32();
                converted = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++) { converted[i * 4] = px[i].r; converted[i * 4 + 1] = px[i].g; converted[i * 4 + 2] = px[i].b; converted[i * 4 + 3] = px[i].a; }
            }
            int sw = source.width, sh = source.height;
            byte[] pixels;
            if (w == sw && h == sh) pixels = converted ?? from.ToArray();
            else if (h >= sh - 3 && w >= sw - 3) pixels = Trim(converted ?? from.ToArray(), sw, sh, w, h, channels);
            else pixels = Shrink(converted ?? from.ToArray(), sw, sh, w, h, channels);
            Lap(1);
            texture.SetPixelData(pixels, 0);
            // Each smaller level averaged from the one above (unsharpened), then sharpened: blending two levels
            // (trilinear) softens a picture, and this gives the sharpness back.
            int pw = w, ph = h;
            var above = pixels;
            for (int level = 1; level < texture.mipmapCount; level++)
            {
                int lw = Math.Max(1, w >> level), lh = Math.Max(1, h >> level);
                above = Shrink(above, pw, ph, lw, lh, channels);
                (pw, ph) = (lw, lh);
                var mip = (byte[])above.Clone();
                Sharpen(mip, lw, lh, channels, MipSharpen);
                texture.SetPixelData(mip, level);
            }
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: !compress && !keepReadable);
            Lap(2);
            if (compress) texture.Compress(HighQuality);
            Lap(3);
            return texture;
        }

        /// <summary>Unsharp mask: each value plus <paramref name="amount"/> times its difference from its 3x3 neighbourhood's mean.</summary>
        private static void Sharpen(byte[] px, int w, int h, int c, float amount)
        {
            if (w < 3 || h < 3) return;
            int stride = w * c, k = (int)Math.Round(amount * 256);
            // Sums of each value and its neighbours across, then down (a value's neighbourhood is cut off at the edges).
            var across = new int[px.Length];
            Parallel.For(0, h, y =>
            {
                int row = y * stride;
                for (int i = 0; i < stride; i++)
                {
                    int x = i / c, sum = px[row + i];
                    if (x > 0) sum += px[row + i - c];
                    if (x < w - 1) sum += px[row + i + c];
                    across[row + i] = sum;
                }
            });
            var src = (byte[])px.Clone();
            Parallel.For(0, h, y =>
            {
                int row = y * stride, ny = (y > 0 ? 1 : 0) + 1 + (y < h - 1 ? 1 : 0);
                for (int i = 0; i < stride; i++)
                {
                    int x = i / c, nx = (x > 0 ? 1 : 0) + 1 + (x < w - 1 ? 1 : 0), n = nx * ny;
                    int sum = across[row + i];
                    if (y > 0) sum += across[row - stride + i];
                    if (y < h - 1) sum += across[row + stride + i];
                    int v = src[row + i];
                    int sharp = v + (((v * n - sum) * k) / n >> 8);
                    px[row + i] = (byte)(sharp < 0 ? 0 : sharp > 255 ? 255 : sharp);
                }
            });
        }

        /// <summary>The middle <paramref name="w"/>x<paramref name="h"/> of a picture (rows run bottom to top).</summary>
        private static byte[] Trim(byte[] src, int sw, int sh, int w, int h, int c)
        {
            var dst = new byte[w * h * c];
            int x0 = (sw - w) / 2, y0 = (sh - h) / 2;
            for (int y = 0; y < h; y++) Buffer.BlockCopy(src, ((y + y0) * sw + x0) * c, dst, y * w * c, w * c);
            return dst;
        }

        /// <summary>
        /// Shrinks a picture by averaging the source area under each new pixel (one pass across, one down), rows spread over
        /// the cores (each row on its own, so the result never depends on the threads).
        /// </summary>
        private static byte[] Shrink(byte[] src, int sw, int sh, int w, int h, int c)
        {
            var (xStart, xWeights) = Taps(sw, w);
            var (yStart, yWeights) = Taps(sh, h);
            // Across: sw x sh -> w x sh, in 16.16 fixed point.
            var mid = new int[w * sh * c];
            Parallel.For(0, sh, y =>
            {
                int row = y * sw * c, outRow = y * w * c;
                for (int x = 0; x < w; x++)
                {
                    var weights = xWeights[x];
                    int s = row + xStart[x] * c, o = outRow + x * c;
                    for (int ch = 0; ch < c; ch++)
                    {
                        int sum = 0;
                        for (int k = 0; k < weights.Length; k++) sum += src[s + k * c + ch] * weights[k];
                        mid[o + ch] = sum;
                    }
                }
            });
            // Down: w x sh -> w x h.
            var dst = new byte[w * h * c];
            int stride = w * c;
            Parallel.For(0, h, y =>
            {
                var weights = yWeights[y];
                int s0 = yStart[y] * stride, o = y * stride;
                for (int i = 0; i < stride; i++)
                {
                    long sum = 0;
                    for (int k = 0; k < weights.Length; k++) sum += (long)mid[s0 + k * stride + i] * weights[k];
                    dst[o + i] = (byte)Math.Min(255, (sum + (1L << 31)) >> 32);
                }
            });
            return dst;
        }

        /// <summary>For each new pixel along one side, the first source pixel under it and each covered pixel's share (summing to 65536).</summary>
        private static (int[] start, int[][] weights) Taps(int from, int to)
        {
            var start = new int[to];
            var weights = new int[to][];
            double scale = (double)from / to;
            for (int i = 0; i < to; i++)
            {
                double a = i * scale, b = (i + 1) * scale;
                int first = (int)Math.Floor(a), last = Math.Min(from - 1, (int)Math.Ceiling(b) - 1);
                var w = new int[last - first + 1];
                int total = 0;
                for (int k = 0; k < w.Length; k++)
                {
                    double overlap = Math.Min(b, first + k + 1) - Math.Max(a, first + k);
                    w[k] = (int)Math.Round(overlap / scale * 65536);
                    total += w[k];
                }
                w[w.Length / 2] += 65536 - total; // rounding: the shares add up exactly
                start[i] = first;
                weights[i] = w;
            }
            return (start, weights);
        }

        // ---- The disk cache --------------------------------------------------------------------------------

        private static string? CachePath(string source, string kind)
        {
            if (CacheFolder == null) return null;
            ulong hash = 14695981039346656037; // FNV-1a of the source's full path
            foreach (char ch in source) { hash ^= ch; hash *= 1099511628211; }
            return Path.Combine(CacheFolder, hash.ToString("x16") + kind);
        }

        /// <summary>For checks: forgets a file's cached pictures (everyday and whole), so the next load decodes it again.</summary>
        public static void Uncache(string source)
        {
            foreach (var kind in new[] { Everyday, Whole })
            {
                var path = CachePath(source, kind);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>For checks: where a file's everyday picture is cached (null with no cache).</summary>
        public static string? CacheFileOf(string source) => CachePath(source, Everyday);

        // Header: magic, version, cap, the source's length and write time (a changed file is made again), width, height,
        // texture format, mip count, data length; then the texture's bytes (every mip level, as Unity keeps them). A file
        // that doesn't read back exactly (cut short, from an older version, damaged) is deleted, to be made again.
        private static Texture2D? ReadCache(string source, string name, string kind)
        {
            var path = CachePath(source, kind);
            if (path == null || !File.Exists(path)) return null;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Texture2D? texture = null;
            bool bad = false;
            try
            {
                var info = new FileInfo(source);
                byte[] data;
                int w, h, mips, length;
                TextureFormat format;
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadUInt32() != CacheMagic || r.ReadInt32() != CacheVersion || r.ReadInt32() != Cap) { bad = true; return null; }
                    if (r.ReadInt64() != info.Length || r.ReadInt64() != info.LastWriteTimeUtc.Ticks) { bad = true; return null; }
                    w = r.ReadInt32();
                    h = r.ReadInt32();
                    format = (TextureFormat)r.ReadInt32();
                    mips = r.ReadInt32();
                    length = r.ReadInt32();
                    if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || mips <= 0 || mips > 14 || length <= 0 || length > r.BaseStream.Length) { bad = true; return null; }
                    data = r.ReadBytes(length);
                }
                if (data.Length != length) { bad = true; return null; }
                if (!SystemInfo.SupportsTextureFormat(format)) return null;
                texture = new Texture2D(w, h, format, mips, false) { name = name, filterMode = FilterMode.Trilinear };
                texture.LoadRawTextureData(data);
                texture.Apply(false, makeNoLongerReadable: true);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow); // recently used: kept when the cache is trimmed
                CacheHits++;
                CacheMs += watch.Elapsed.TotalMilliseconds;
                return texture;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is UnityException || e is ArgumentException)
            {
                bad = true;
                if (texture != null) UnityEngine.Object.Destroy(texture);
                return null;
            }
            finally
            {
                if (bad)
                    try { File.Delete(path); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        private static void WriteCache(string source, Texture2D texture, string kind)
        {
            var path = CachePath(source, kind);
            if (path == null) return;
            // Its own temporary name, so two writers (two copies of the game) never write into one file.
            var temp = $"{path}.{System.Diagnostics.Process.GetCurrentProcess().Id}.{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(CacheFolder!);
                var info = new FileInfo(source);
                var data = texture.GetRawTextureData<byte>();
                using (var w = new BinaryWriter(File.Create(temp)))
                {
                    w.Write(CacheMagic);
                    w.Write(CacheVersion);
                    w.Write(Cap);
                    w.Write(info.Length);
                    w.Write(info.LastWriteTimeUtc.Ticks);
                    w.Write(texture.width);
                    w.Write(texture.height);
                    w.Write((int)texture.format);
                    w.Write(texture.mipmapCount);
                    w.Write(data.Length);
                    w.Write(data.ToArray());
                }
                // Into place whole: a reader sees the old file or the new one, never half of one.
                if (File.Exists(path)) File.Replace(temp, path, null);
                else
                    try { File.Move(temp, path); }
                    catch (IOException) when (File.Exists(path)) { File.Replace(temp, path, null); } // another writer got there first
                if ((_written += data.Length) > TrimEvery)
                {
                    _written = 0;
                    TrimCacheSoon();
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is PlatformNotSupportedException)
            {
                Debug.LogWarning($"Couldn't cache a card picture in {CacheFolder}: {e.Message}");
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Trims the disk cache back under its budgets, least recently used first, and clears temporary files a writer
        /// left behind (older than ten minutes), off the main thread. One trim at a time.
        /// </summary>
        public static void TrimCacheSoon()
        {
            var folder = CacheFolder;
            if (folder == null || !Directory.Exists(folder)) return;
            if (System.Threading.Interlocked.Exchange(ref _trimming, 1) == 1) return;
            Task.Run(() =>
            {
                try
                {
                    var dir = new DirectoryInfo(folder);
                    foreach (var (kind, budget) in new[] { (Everyday, CacheBudget), (Whole, FullBudget) })
                    {
                        long total = 0;
                        foreach (var f in dir.GetFiles("*" + kind).OrderByDescending(f => f.LastWriteTimeUtc))
                        {
                            total += f.Length;
                            if (total > budget) f.Delete();
                        }
                    }
                    var stale = DateTime.UtcNow.AddMinutes(-10);
                    foreach (var f in dir.GetFiles("*.tmp"))
                        if (f.LastWriteTimeUtc < stale) f.Delete();
                }
                catch (Exception) { /* a cache: trimming can wait for the next time */ }
                finally { System.Threading.Interlocked.Exchange(ref _trimming, 0); }
            });
        }
    }
}
