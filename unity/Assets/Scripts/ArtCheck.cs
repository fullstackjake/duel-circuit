using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace YgoCampaign.Game
{
    /// <summary>
    /// `-artcheck <folder>`: measures card pictures the old way and the new (see <see cref="ArtLoader"/>) for cards that have
    /// both an EDOPro-size picture and an HD one: the time to load each, and its memory. Writes art.txt in the folder and
    /// the log, then quits.
    /// </summary>
    public sealed class ArtCheck : MonoBehaviour
    {
        private const int Sample = 40;

        public static void Run(GameObject host, string folder) => host.AddComponent<ArtCheck>().StartCoroutine(host.GetComponent<ArtCheck>().Measure(folder));

        private IEnumerator Measure(string folder)
        {
            yield return new WaitForSecondsRealtime(1);
            var art = CardView.Art;
            var lines = new List<string>();
            void Line(string text) { lines.Add(text); Debug.Log("art check: " + text); }
            if (art == null || art.Folders.Count < 2)
            {
                Line("needs an HD pictures folder (hd_pics in settings.txt, or -hdpics)");
                Finish(folder, lines);
                yield break;
            }
            var own = art.Folders[0];
            var pairs = new List<(string old, string hd)>();
            foreach (var file in Directory.GetFiles(own, "*.jpg").OrderBy(f => f, System.StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                var hd = art.Folders.Skip(1).Select(f => Path.Combine(f, name)).FirstOrDefault(File.Exists);
                if (hd == null || ArtLoader.ImageSize(hd).height < 1000) continue;
                pairs.Add((file, hd));
                if (pairs.Count == Sample) break;
            }
            Line($"{pairs.Count} cards with an old picture ({own}) and an HD one; cap {ArtLoader.Cap}, cache {ArtLoader.CacheFolder}");
            if (pairs.Count == 0)
            {
                Line("no card has both an old picture and an HD one: nothing to measure");
                Finish(folder, lines);
                yield break;
            }
            var (ow, oh) = ArtLoader.ImageSize(pairs[0].old);
            var (hw, hh) = ArtLoader.ImageSize(pairs[0].hd);
            Line($"sizes: old {ow}x{oh}, HD {hw}x{hh}");

            Time("old picture, old way (decode, no mipmaps)", pairs, p => ArtLoader.LoadPlain(p.old, "old"), Line);
            Time("old picture, new way (plus mipmaps)", pairs, p => ArtLoader.Load(p.old, "old new"), Line);
            Time("HD picture, old way (decode only, full size)", pairs, p => ArtLoader.LoadPlain(p.hd, "hd old"), Line);
            foreach (var p in pairs) ArtLoader.Uncache(p.hd);
            System.Array.Clear(ArtLoader.StageMs, 0, 4);
            Time("HD picture, new way, first time (decode, shrink, mipmaps, compress, cache)", pairs, p => ArtLoader.Load(p.hd, "hd new"), Line);
            Line($"  first time, per picture: decode {ArtLoader.StageMs[0] / pairs.Count:0.0} ms, shrink {ArtLoader.StageMs[1] / pairs.Count:0.0}, mipmaps {ArtLoader.StageMs[2] / pairs.Count:0.0}, compress {ArtLoader.StageMs[3] / pairs.Count:0.0}");
            ArtLoader.HighQuality = false;
            foreach (var p in pairs) ArtLoader.Uncache(p.hd);
            Time("HD picture, new way, first time, quick compressor", pairs, p => ArtLoader.Load(p.hd, "hd quick"), Line);
            ArtLoader.HighQuality = true;
            foreach (var p in pairs) ArtLoader.Uncache(p.hd);
            Time("HD picture, new way, first time again", pairs, p => ArtLoader.Load(p.hd, "hd new"), Line);
            Time("HD picture, new way, from the disk cache", pairs, p => ArtLoader.Load(p.hd, "hd cached"), Line);
            Time("HD picture, whole, first time (big views: decode, mipmaps, cache)", pairs, p => ArtLoader.LoadFull(p.hd, "hd full"), Line);
            Time("HD picture, whole, from the disk cache", pairs, p => ArtLoader.LoadFull(p.hd, "hd full cached"), Line);

            // A reader opening: CardArt.GetFull, new cards (nothing cached), then the same cards again after the
            // in-memory pictures are dropped (from the disk cache). Dropping whole pictures frees them as it goes.
            var codes = pairs.Take(12).Select(p => uint.Parse(Path.GetFileNameWithoutExtension(p.hd))).ToList();
            foreach (var p in pairs.Take(12)) ArtLoader.Uncache(p.hd);
            art.ScanStyles();
            TimeCodes("reader opening (GetFull), card not cached", codes, c => art.GetFull(c), Line);
            yield return null;
            art.ScanStyles(); // forgets every picture in memory
            yield return null;
            TimeCodes("reader opening (GetFull), card cached on disk", codes, c => art.GetFull(c), Line);
            art.ScanStyles();

            // A damaged cache file (cut short) is thrown away and made again, with no exception and no leak.
            var damaged = pairs[0].hd;
            var before = ArtLoader.Load(damaged, "before damage"); // makes sure the cache file is there
            if (before != null) Destroy(before);
            var cacheFile = ArtLoader.CacheFileOf(damaged);
            if (cacheFile != null && File.Exists(cacheFile))
            {
                long full = new FileInfo(cacheFile).Length;
                using (var f = new FileStream(cacheFile, FileMode.Open, FileAccess.Write)) f.SetLength(full / 2);
                int decoded = ArtLoader.Decoded;
                var reloaded = ArtLoader.Load(damaged, "after damage");
                long now = File.Exists(cacheFile) ? new FileInfo(cacheFile).Length : 0;
                Line($"damaged cache file ({full} bytes cut to {full / 2}): picture {(reloaded != null ? $"{reloaded.width}x{reloaded.height}" : "MISSING")}, "
                     + $"decoded again {ArtLoader.Decoded - decoded == 1}, cache file remade at {now} bytes: {(reloaded != null && now == full ? "ok" : "FAIL")}");
                if (reloaded != null) Destroy(reloaded);
            }
            else Line("damaged cache file: no cache file to damage (FAIL)");
            Finish(folder, lines);
        }

        private static void Time(string label, List<(string old, string hd)> pairs, System.Func<(string old, string hd), Texture2D?> load, System.Action<string> line)
        {
            if (pairs.Count == 0) { line($"{label}: nothing to time"); return; }
            var times = new List<double>();
            Texture2D? last = null;
            foreach (var p in pairs)
            {
                var watch = Stopwatch.StartNew();
                var texture = load(p);
                times.Add(watch.Elapsed.TotalMilliseconds);
                if (last != null) Destroy(last);
                last = texture;
            }
            times.Sort();
            string memory = last == null ? "none" : $"{last.width}x{last.height} {last.format}, {last.mipmapCount} mips, "
                + $"{Profiler.GetRuntimeMemorySizeLong(last) / 1024.0:0} KB (profiler), {Estimate(last) / 1024.0:0} KB (estimate)";
            line($"{label}: median {times[times.Count / 2]:0.00} ms, mean {times.Average():0.00} ms, max {times[times.Count - 1]:0.00} ms; {memory}");
            if (last != null) Destroy(last);
        }

        /// <summary>Times a CardArt lookup per card (the textures are CardArt's own: not destroyed here).</summary>
        private static void TimeCodes(string label, List<uint> codes, System.Func<uint, Texture2D?> load, System.Action<string> line)
        {
            if (codes.Count == 0) { line($"{label}: nothing to time"); return; }
            var times = new List<double>();
            foreach (var code in codes)
            {
                var watch = Stopwatch.StartNew();
                load(code);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            line($"{label}: median {times[times.Count / 2]:0.00} ms, mean {times.Average():0.00} ms, max {times[times.Count - 1]:0.00} ms ({codes.Count} cards)");
        }

        /// <summary>GPU bytes: RGB24 is held as RGBA (Metal has no 3-byte format), DXT1 half a byte a pixel, DXT5 one; mipmaps add a third.</summary>
        private static long Estimate(Texture2D t)
        {
            double perPixel = t.format switch { TextureFormat.DXT1 => 0.5, TextureFormat.DXT5 => 1, _ => 4 };
            return (long)(t.width * t.height * perPixel * (t.mipmapCount > 1 ? 4.0 / 3 : 1));
        }

        private static void Finish(string folder, List<string> lines)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "art.txt"), lines);
            Application.Quit();
        }
    }
}
