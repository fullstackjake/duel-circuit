using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Ygo.Harness;

/// <summary>
/// `--download-pictures <cardinfo.json> <folder> [--limit N]`: fetches the small card pictures (168x246) that aren't in
/// the folder yet from YGOPRODeck, once, for local use (they stay in the git-ignored mods folder and are never shared).
/// Polite: a few requests at a time, at most ~8 per second, skips pictures already there, and can be stopped and
/// resumed (each file is written to a temporary name first).
/// </summary>
public static class PictureDownload
{
    private const int Parallel = 4;
    private const double MaxPerSecond = 8;

    public static int Run(string cardInfoPath, string folder, int limit)
    {
        Directory.CreateDirectory(folder);
        var have = new HashSet<uint>();
        foreach (var file in Directory.GetFiles(folder))
            if (uint.TryParse(Path.GetFileNameWithoutExtension(file), out var code)) have.Add(code);

        // The card's main picture (alternate artworks fall back to it in the game).
        using var doc = JsonDocument.Parse(File.ReadAllBytes(cardInfoPath));
        var wanted = new List<(uint id, string url)>();
        foreach (var card in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var type = card.GetProperty("type").GetString() ?? "";
            if (type.Contains("Skill") || type.Contains("Token")) continue;
            var id = card.GetProperty("id").GetUInt32();
            if (have.Contains(id)) continue;
            foreach (var image in card.GetProperty("card_images").EnumerateArray())
                if (image.GetProperty("id").GetUInt32() == id && image.TryGetProperty("image_url_small", out var url))
                {
                    wanted.Add((id, url.GetString()!));
                    break;
                }
        }
        if (limit > 0 && wanted.Count > limit) wanted = wanted.Take(limit).ToList();
        Console.WriteLine($"{have.Count} pictures already in {folder}; downloading {wanted.Count} (small, ~25 KB each).");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DuelCircuit/0.1 (personal fan project; one-time local picture download)");
        var timer = Stopwatch.StartNew();
        var gate = new SemaphoreSlim(Parallel);
        var pace = new object();
        double nextSlot = 0;
        int done = 0, failed = 0;
        long bytes = 0;

        async Task One((uint id, string url) item)
        {
            await gate.WaitAsync();
            try
            {
                // Space requests out to at most MaxPerSecond overall.
                double wait;
                lock (pace)
                {
                    var now = timer.Elapsed.TotalSeconds;
                    nextSlot = Math.Max(nextSlot, now) + 1.0 / MaxPerSecond;
                    wait = nextSlot - 1.0 / MaxPerSecond - now;
                }
                if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait));

                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        using var response = await http.GetAsync(item.url);
                        if ((int)response.StatusCode == 429) { await Task.Delay(TimeSpan.FromSeconds(10 * attempt)); continue; }
                        response.EnsureSuccessStatusCode();
                        var data = await response.Content.ReadAsByteArrayAsync();
                        if (data.Length < 1000 || data[0] != 0xFF || data[1] != 0xD8) throw new InvalidDataException("not a JPEG");
                        var path = Path.Combine(folder, $"{item.id}.jpg");
                        var temp = path + ".part";
                        await File.WriteAllBytesAsync(temp, data);
                        File.Move(temp, path, overwrite: true);
                        Interlocked.Add(ref bytes, data.Length);
                        break;
                    }
                    catch (Exception ex) when (attempt < 3 && ex is not InvalidDataException)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        Console.WriteLine($"  {item.id}: {ex.Message}");
                        break;
                    }
                }
                var n = Interlocked.Increment(ref done);
                if (n % 250 == 0 || n == wanted.Count)
                    Console.WriteLine($"  {n}/{wanted.Count}  ({Interlocked.Read(ref bytes) / 1048576.0:F0} MB, {timer.Elapsed:mm\\:ss}, {failed} failed)");
            }
            finally
            {
                gate.Release();
            }
        }

        Task.WaitAll(wanted.Select(One).ToArray());
        Console.WriteLine($"Done: {done - failed} pictures ({bytes / 1048576.0:F0} MB) in {timer.Elapsed:mm\\:ss}; {failed} failed.");
        return failed == 0 ? 0 : 1;
    }
}
